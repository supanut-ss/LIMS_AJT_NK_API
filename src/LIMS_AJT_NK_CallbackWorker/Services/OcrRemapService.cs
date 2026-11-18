using System.Data;
using System.Text.Json.Nodes;
using LIMS_AJT_NK_CallbackWorker.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_CallbackWorker.Services;

// Calls dbo.usp_ocr_callback_remap directly against the shared LIMS_NK DB, the
// same procedure LIMS_AJT_NK_API's call_back flow and WMS_NEW's Review COA page
// use. The Worker already shares that DB (see WorkerDbContext), so it does not
// need an HTTP call into the API to force a callback to be remapped.
//
// The procedure only does matching/QAQC updates (SQL Server cannot touch the
// filesystem). When a page newly matches an inbound receipt here, the COA
// document was never copied during the original (failed) callback, so this
// service also stores it - mirroring OcrCallbackInterfaceService.StoreDocumentsAsync
// in LIMS_AJT_NK_API - and rewrites interface_summary_json with the document
// fields, exactly like the API does after a successful call_back.
public interface IOcrRemapService
{
    Task<OcrRemapResult> RemapAsync(
        Guid callbackId,
        bool forceReprocess,
        CancellationToken cancellationToken = default);
}

public sealed record OcrRemapResult(string SummaryJson, bool IsReplay);

public sealed class OcrRemapService(WorkerDbContext dbContext, ILogger<OcrRemapService> logger) : IOcrRemapService
{
    private const string InterfaceUser = "worker";

    public async Task<OcrRemapResult> RemapAsync(
        Guid callbackId,
        bool forceReprocess,
        CancellationToken cancellationToken = default)
    {
        var connection = (SqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var createdDocumentPaths = new List<string>();

        try
        {
            var (summaryJson, isReplay) = await ExecuteRemapProcedureAsync(
                connection, transaction, callbackId, forceReprocess, cancellationToken);

            if (!isReplay)
            {
                summaryJson = await StoreDocumentsAsync(
                    connection, transaction, callbackId, summaryJson, createdDocumentPaths, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new OcrRemapResult(summaryJson, isReplay);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DeleteCreatedDocuments(createdDocumentPaths);
            throw;
        }
    }

    private static async Task<(string SummaryJson, bool IsReplay)> ExecuteRemapProcedureAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid callbackId,
        bool forceReprocess,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "dbo.usp_ocr_callback_remap";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@callback_id", SqlDbType.UniqueIdentifier) { Value = callbackId });
        command.Parameters.Add(new SqlParameter("@force_reprocess", SqlDbType.Bit) { Value = forceReprocess });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"OCR callback '{callbackId}' was not found.");
        }

        return (reader.GetString(0), reader.GetBoolean(1));
    }

    private async Task<string> StoreDocumentsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid callbackId,
        string summaryJson,
        ICollection<string> createdDocumentPaths,
        CancellationToken cancellationToken)
    {
        var summary = JsonNode.Parse(summaryJson)!.AsObject();
        var pages = summary["pages"]?.AsArray();
        if (pages is null)
        {
            return summaryJson;
        }

        var eligiblePages = pages
            .Select(x => x!.AsObject())
            .Where(x => !string.IsNullOrEmpty(x["receipt_detail_id"]?.GetValue<string?>())
                && x["status"]?.GetValue<string?>() != "ignored")
            .ToList();

        if (eligiblePages.Count == 0)
        {
            return summaryJson;
        }

        var jobTaskId = await ExecuteScalarAsync<string>(
            connection, transaction,
            "SELECT job_task_id FROM dbo.t_interface_lims_ocr_callback WHERE callback_id = @callback_id",
            [new SqlParameter("@callback_id", SqlDbType.UniqueIdentifier) { Value = callbackId }],
            cancellationToken)
            ?? throw new InvalidOperationException($"OCR callback '{callbackId}' was not found.");

        var fileIdsByPage = await LoadResultFileIdsAsync(connection, transaction, callbackId, cancellationToken);

        string? documentHostDirectory = null;
        string? documentGroupConfig = null;
        string? documentWebPathConfig = null;

        await using (var configCommand = connection.CreateCommand())
        {
            configCommand.Transaction = transaction;
            configCommand.CommandText =
                "SELECT TOP (1) document_host_directory, document_group, document_web_path " +
                "FROM dbo.t_interface_lims_ocr_config_api ORDER BY create_date DESC";

            await using var reader = await configCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                documentHostDirectory = reader.IsDBNull(0) ? null : reader.GetString(0);
                documentGroupConfig = reader.IsDBNull(1) ? null : reader.GetString(1);
                documentWebPathConfig = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
        }

        if (string.IsNullOrWhiteSpace(documentHostDirectory))
        {
            foreach (var page in eligiblePages)
            {
                page["document_status"] = "not_configured";
            }

            return summary.ToJsonString();
        }

        var documentGroup = string.IsNullOrWhiteSpace(documentGroupConfig) ? "QC_COA" : documentGroupConfig.Trim();
        ValidateDocumentGroup(documentGroup);

        // The worker moves the PDF from Processing to Success/Error after the
        // callback, so file_path can be stale; final_path holds the moved location.
        string? logFilePath = null;
        string? logFinalPath = null;
        await using (var pathCommand = connection.CreateCommand())
        {
            pathCommand.Transaction = transaction;
            pathCommand.CommandText =
                "SELECT TOP (1) file_path, final_path FROM dbo.t_interface_lims_ocr_log " +
                "WHERE (api_name = 'input_ocr' OR api_name = 'input_ocr_file') AND job_task_id = @job_task_id AND file_path IS NOT NULL " +
                "ORDER BY create_date DESC";
            pathCommand.Parameters.Add(new SqlParameter("@job_task_id", SqlDbType.NVarChar, 100) { Value = jobTaskId });

            await using var pathReader = await pathCommand.ExecuteReaderAsync(cancellationToken);
            if (await pathReader.ReadAsync(cancellationToken))
            {
                logFilePath = pathReader.IsDBNull(0) ? null : pathReader.GetString(0);
                logFinalPath = pathReader.IsDBNull(1) ? null : pathReader.GetString(1);
            }
        }

        if (string.IsNullOrWhiteSpace(logFilePath) && string.IsNullOrWhiteSpace(logFinalPath))
        {
            throw new InvalidOperationException($"Input OCR file log was not found for job_task_id '{jobTaskId}'.");
        }

        var sourcePath = new[] { logFinalPath, logFilePath }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!))
            .FirstOrDefault(File.Exists);

        if (sourcePath is null)
        {
            throw new FileNotFoundException(
                "Input OCR source file was not found.",
                Path.GetFullPath(string.IsNullOrWhiteSpace(logFinalPath) ? logFilePath! : logFinalPath));
        }

        var hostRoot = Path.GetFullPath(documentHostDirectory.Trim());
        var targetDirectory = Path.GetFullPath(Path.Combine(hostRoot, documentGroup));
        EnsurePathIsWithinRoot(hostRoot, targetDirectory);
        Directory.CreateDirectory(targetDirectory);

        var webRoot = string.IsNullOrWhiteSpace(documentWebPathConfig) ? "../_Documents" : documentWebPathConfig.Trim();
        var originalDocumentName = GetOriginalFileName(sourcePath);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var documentsByReceipt = new Dictionary<Guid, (int DocumentId, string DocumentPath)>();

        foreach (var page in eligiblePages)
        {
            var receiptDetailId = Guid.Parse(page["receipt_detail_id"]!.GetValue<string>());
            var pageId = page["page_id"]!.GetValue<int>();

            if (documentsByReceipt.TryGetValue(receiptDetailId, out var current))
            {
                page["document_status"] = "stored";
                page["document_id"] = current.DocumentId;
                page["document_path"] = current.DocumentPath;
                continue;
            }

            var existing = await LoadExistingDocumentAsync(
                connection, transaction, receiptDetailId, documentGroup, jobTaskId, cancellationToken);

            if (existing is not null)
            {
                documentsByReceipt[receiptDetailId] = existing.Value;
                page["document_status"] = "already_exists";
                page["document_id"] = existing.Value.DocumentId;
                page["document_path"] = existing.Value.DocumentPath;
                continue;
            }

            var storedFileName = $"{receiptDetailId:N}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
            var destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, storedFileName));
            EnsurePathIsWithinRoot(targetDirectory, destinationPath);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            createdDocumentPaths.Add(destinationPath);

            var documentPath = BuildWebPath(webRoot, documentGroup, storedFileName);
            fileIdsByPage.TryGetValue(pageId, out var resultIds);

            var documentId = await InsertDocumentAsync(
                connection, transaction, receiptDetailId, documentGroup, GetContentType(extension),
                originalDocumentName, documentPath, jobTaskId, callbackId, resultIds.FileId ?? resultIds.TrackingId,
                cancellationToken);

            documentsByReceipt[receiptDetailId] = (documentId, documentPath);
            await MarkScanCoaProcessingAsync(connection, transaction, receiptDetailId, documentGroup, cancellationToken);
            page["document_status"] = "stored";
            page["document_id"] = documentId;
            page["document_path"] = documentPath;
        }

        var enrichedJson = summary.ToJsonString();

        await using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText =
            "UPDATE dbo.t_interface_lims_ocr_callback SET interface_summary_json = @json WHERE callback_id = @callback_id";
        updateCommand.Parameters.Add(new SqlParameter("@json", SqlDbType.NVarChar, -1) { Value = enrichedJson });
        updateCommand.Parameters.Add(new SqlParameter("@callback_id", SqlDbType.UniqueIdentifier) { Value = callbackId });
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);

        return enrichedJson;
    }

    // Mirrors QAQCPostFileToServer.ashx: attaching a COA document moves the QAQC
    // scan's COA status from NONE to PROCESSING for the matching document group.
    private static async Task MarkScanCoaProcessingAsync(
        SqlConnection connection, SqlTransaction transaction, Guid receiptDetailId, string documentGroup,
        CancellationToken cancellationToken)
    {
        var column = documentGroup switch
        {
            "QC_COA" => "qc_coa_status",
            "QA_COA" => "qa_coa_status",
            _ => null
        };

        if (column is null)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"UPDATE dbo.t_lims_qaqc_scan SET {column} = 'PROCESSING' " +
            $"WHERE receipt_detail_id = @receipt_detail_id AND ({column} IS NULL OR {column} = 'NONE')";
        command.Parameters.Add(new SqlParameter("@receipt_detail_id", SqlDbType.UniqueIdentifier) { Value = receiptDetailId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<int, (string? FileId, string? TrackingId)>> LoadResultFileIdsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid callbackId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, (string? FileId, string? TrackingId)>();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT page_id, file_id, tracking_id FROM dbo.t_interface_lims_ocr_result WHERE callback_id = @callback_id";
        command.Parameters.Add(new SqlParameter("@callback_id", SqlDbType.UniqueIdentifier) { Value = callbackId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetInt32(0)] = (
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2));
        }

        return result;
    }

    private static async Task<(int DocumentId, string DocumentPath)?> LoadExistingDocumentAsync(
        SqlConnection connection, SqlTransaction transaction, Guid receiptDetailId, string documentGroup,
        string jobTaskId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT TOP (1) document_id, document_path FROM dbo.t_lims_documents " +
            "WHERE pk_id = @pk_id AND document_group = @document_group AND udf1 = @job_task_id AND ISNULL(is_active, 1) = 1";
        command.Parameters.Add(new SqlParameter("@pk_id", SqlDbType.UniqueIdentifier) { Value = receiptDetailId });
        command.Parameters.Add(new SqlParameter("@document_group", SqlDbType.NVarChar, 64) { Value = documentGroup });
        command.Parameters.Add(new SqlParameter("@job_task_id", SqlDbType.NVarChar, 100) { Value = jobTaskId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (reader.GetInt32(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1));
    }

    private async Task<int> InsertDocumentAsync(
        SqlConnection connection, SqlTransaction transaction, Guid receiptDetailId, string documentGroup,
        string contentType, string documentName, string documentPath, string jobTaskId, Guid callbackId,
        string? udf3, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO dbo.t_lims_documents
                (pk_id, document_group, document_type, document_code, document_name, document_path,
                 description, udf1, udf2, udf3, is_active, create_by, create_date)
            VALUES
                (@pk_id, @document_group, @document_type, '--', @document_name, @document_path,
                 'OCR callback document', @udf1, @udf2, @udf3, 1, @create_by, GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        command.Parameters.Add(new SqlParameter("@pk_id", SqlDbType.UniqueIdentifier) { Value = receiptDetailId });
        command.Parameters.Add(new SqlParameter("@document_group", SqlDbType.NVarChar, 64) { Value = documentGroup });
        command.Parameters.Add(new SqlParameter("@document_type", SqlDbType.NVarChar, 256) { Value = contentType });
        command.Parameters.Add(new SqlParameter("@document_name", SqlDbType.NVarChar, 256) { Value = documentName });
        command.Parameters.Add(new SqlParameter("@document_path", SqlDbType.NVarChar, 1024) { Value = documentPath });
        command.Parameters.Add(new SqlParameter("@udf1", SqlDbType.NVarChar, 512) { Value = jobTaskId });
        command.Parameters.Add(new SqlParameter("@udf2", SqlDbType.NVarChar, 512) { Value = callbackId.ToString() });
        command.Parameters.Add(new SqlParameter("@udf3", SqlDbType.NVarChar, 512) { Value = (object?)udf3 ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@create_by", SqlDbType.NVarChar, 25) { Value = InterfaceUser });

        var documentId = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(documentId);
    }

    private static async Task<T?> ExecuteScalarAsync<T>(
        SqlConnection connection, SqlTransaction transaction, string sql, SqlParameter[] parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? default : (T)value;
    }

    private static void ValidateDocumentGroup(string documentGroup)
    {
        if (documentGroup is "." or ".."
            || documentGroup.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InvalidOperationException("OCR document_group must be a single directory name.");
        }
    }

    private static void EnsurePathIsWithinRoot(string rootPath, string candidatePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidatePath);

        if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OCR document path is outside the configured Host directory.");
        }
    }

    private static string BuildWebPath(string webRoot, string documentGroup, string storedFileName)
    {
        var normalizedRoot = webRoot.Replace('\\', '/').TrimEnd('/');
        return $"{normalizedRoot}/{documentGroup}/{storedFileName}";
    }

    private static string GetOriginalFileName(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        const int workerPrefixLength = 51;

        if (fileName.Length > workerPrefixLength && fileName[17] == '_' && fileName[50] == '_')
        {
            return fileName[workerPrefixLength..];
        }

        return fileName;
    }

    private static string GetContentType(string extension)
    {
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/octet-stream";
    }

    private void DeleteCreatedDocuments(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete created document {Path} during rollback.", path);
            }
        }
    }
}
