using System.Data;
using System.Text.Json;
using LIMS_AJT_NK_API.Data;
using LIMS_AJT_NK_API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LIMS_AJT_NK_API.Services;

public interface IOcrCallbackInterfaceService
{
    Task<OcrCallbackInterfaceSummary> ProcessAsync(
        Guid callbackId,
        CancellationToken cancellationToken = default,
        bool forceReprocess = false);
}

// The inbound/QAQC matching and parameter-update logic that used to live here in
// C# now lives in dbo.usp_ocr_callback_remap (see
// database/20261117_add_usp_ocr_callback_remap.sql), so WMS_NEW (Web) and
// LIMS_AJT_NK_CallbackWorker (Worker) can remap a callback directly against the
// shared DB without an HTTP call into this API. This service's job is now only
// to invoke that procedure and, for this API's own call_back flow, store the
// matched COA document on disk - something the stored procedure cannot do.
public sealed class OcrCallbackInterfaceService(
    ApplicationDbContext dbContext,
    ILogger<OcrCallbackInterfaceService> logger) : IOcrCallbackInterfaceService
{
    private const string InterfaceUser = "api";

    public async Task<OcrCallbackInterfaceSummary> ProcessAsync(
        Guid callbackId,
        CancellationToken cancellationToken = default,
        bool forceReprocess = false)
    {
        // The controller and service can share a DbContext. Matching now runs
        // entirely inside the stored procedure, but SaveChangesAsync (for the
        // document rows below) would still flush any unrelated staged changes
        // on this context, so refuse to run with any already pending.
        if (dbContext.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "OCR callback processing requires a DbContext without unrelated pending changes.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var createdDocumentPaths = new List<string>();

        try
        {
            var (summaryJson, isReplay) = await ExecuteRemapProcedureAsync(
                callbackId, forceReprocess, cancellationToken);

            var summary = JsonSerializer.Deserialize<OcrCallbackInterfaceSummary>(summaryJson)
                ?? throw new InvalidOperationException(
                    $"OCR callback '{callbackId}' returned an invalid interface summary.");
            summary.IsReplay = isReplay;

            if (isReplay)
            {
                await transaction.CommitAsync(cancellationToken);
                return summary;
            }

            await StoreDocumentsAndEnrichSummaryAsync(
                callbackId, summary, createdDocumentPaths, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Processed OCR callback {CallbackId}. Status={Status}, Updated={Updated}, Skipped={Skipped}",
                callbackId,
                summary.InterfaceStatus,
                summary.UpdatedItemCount,
                summary.SkippedItemCount);

            return summary;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            DeleteCreatedDocuments(createdDocumentPaths);
            throw;
        }
    }

    private async Task<(string SummaryJson, bool IsReplay)> ExecuteRemapProcedureAsync(
        Guid callbackId,
        bool forceReprocess,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.usp_ocr_callback_remap";
        command.CommandType = CommandType.StoredProcedure;

        var currentTransaction = dbContext.Database.CurrentTransaction;
        if (currentTransaction is not null)
        {
            command.Transaction = (SqlTransaction)currentTransaction.GetDbTransaction();
        }

        command.Parameters.Add(new SqlParameter("@callback_id", SqlDbType.UniqueIdentifier) { Value = callbackId });
        command.Parameters.Add(new SqlParameter("@force_reprocess", SqlDbType.Bit) { Value = forceReprocess });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"OCR callback '{callbackId}' was not found.");
        }

        var summaryJson = reader.GetString(0);
        var isReplay = reader.GetBoolean(1);
        return (summaryJson, isReplay);
    }

    private async Task StoreDocumentsAndEnrichSummaryAsync(
        Guid callbackId,
        OcrCallbackInterfaceSummary summary,
        ICollection<string> createdDocumentPaths,
        CancellationToken cancellationToken)
    {
        var eligiblePages = summary.Pages
            .Where(x => x.ReceiptDetailId.HasValue && x.Status != "ignored")
            .ToList();

        if (eligiblePages.Count == 0)
        {
            return;
        }

        var callback = await dbContext.InterfaceLimsOcrCallbacks
            .AsNoTracking()
            .Where(x => x.CallbackId == callbackId)
            .Select(x => new { x.JobTaskId })
            .SingleAsync(cancellationToken);

        var resultFileIds = await dbContext.InterfaceLimsOcrResults
            .AsNoTracking()
            .Where(x => x.CallbackId == callbackId)
            .Select(x => new { x.PageId, x.FileId, x.TrackingId })
            .ToDictionaryAsync(x => x.PageId, cancellationToken);

        var config = await dbContext.InterfaceLimsOcrConfigApis
            .AsNoTracking()
            .OrderByDescending(x => x.CreateDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(config?.DocumentHostDirectory))
        {
            foreach (var page in eligiblePages)
            {
                page.DocumentStatus = "not_configured";
            }

            await PersistEnrichedSummaryAsync(callbackId, summary, cancellationToken);
            return;
        }

        var documentGroup = string.IsNullOrWhiteSpace(config.DocumentGroup)
            ? "QC_COA"
            : config.DocumentGroup.Trim();
        ValidateDocumentGroup(documentGroup);

        var sourceLog = await dbContext.InterfaceLimsOcrLogs
            .AsNoTracking()
            .Where(x => (x.ApiName == "input_ocr" || x.ApiName == "input_ocr_file")
                && x.JobTaskId == callback.JobTaskId
                && x.FilePath != null)
            .OrderByDescending(x => x.CreateDate)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"Input OCR file log was not found for job_task_id '{callback.JobTaskId}'.");

        // The worker moves the PDF to Success/Error after the callback, so FilePath
        // can be stale; FinalPath holds the moved location.
        var sourcePath = new[] { sourceLog.FinalPath, sourceLog.FilePath }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!))
            .FirstOrDefault(File.Exists);

        if (sourcePath is null)
        {
            throw new FileNotFoundException(
                "Input OCR source file was not found.",
                Path.GetFullPath(sourceLog.FinalPath ?? sourceLog.FilePath!));
        }

        var hostRoot = Path.GetFullPath(config.DocumentHostDirectory.Trim());
        var targetDirectory = Path.GetFullPath(Path.Combine(hostRoot, documentGroup));
        EnsurePathIsWithinRoot(hostRoot, targetDirectory);
        Directory.CreateDirectory(targetDirectory);

        var webRoot = string.IsNullOrWhiteSpace(config.DocumentWebPath)
            ? "../_Documents"
            : config.DocumentWebPath.Trim();
        var originalDocumentName = GetOriginalFileName(sourcePath);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var documentsByReceipt = new Dictionary<Guid, LimsDocumentEntity>();

        foreach (var page in eligiblePages)
        {
            var receiptDetailId = page.ReceiptDetailId!.Value;
            if (documentsByReceipt.TryGetValue(receiptDetailId, out var currentDocument))
            {
                page.DocumentStatus = "stored";
                page.DocumentId = currentDocument.DocumentId;
                page.DocumentPath = currentDocument.DocumentPath;
                continue;
            }

            var existingDocument = await dbContext.LimsDocuments
                .FirstOrDefaultAsync(x => x.PkId == receiptDetailId
                    && x.DocumentGroup == documentGroup
                    && x.Udf1 == callback.JobTaskId
                    && x.IsActive != false,
                    cancellationToken);

            if (existingDocument is not null)
            {
                documentsByReceipt[receiptDetailId] = existingDocument;
                page.DocumentStatus = "already_exists";
                page.DocumentId = existingDocument.DocumentId;
                page.DocumentPath = existingDocument.DocumentPath;
                continue;
            }

            var storedFileName = $"{receiptDetailId:N}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
            var destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, storedFileName));
            EnsurePathIsWithinRoot(targetDirectory, destinationPath);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            createdDocumentPaths.Add(destinationPath);

            resultFileIds.TryGetValue(page.PageId, out var resultIds);
            var document = new LimsDocumentEntity
            {
                PkId = receiptDetailId,
                DocumentGroup = documentGroup,
                DocumentType = GetContentType(extension),
                DocumentCode = "--",
                DocumentName = originalDocumentName,
                DocumentPath = BuildWebPath(webRoot, documentGroup, storedFileName),
                Description = "OCR callback document",
                Udf1 = callback.JobTaskId,
                Udf2 = callbackId.ToString(),
                Udf3 = resultIds?.FileId ?? resultIds?.TrackingId,
                IsActive = true,
                CreateBy = InterfaceUser,
                CreateDate = DateTime.Now
            };

            dbContext.LimsDocuments.Add(document);
            await MarkScanCoaProcessingAsync(receiptDetailId, documentGroup, cancellationToken);
            documentsByReceipt[receiptDetailId] = document;
            page.DocumentStatus = "stored";
            page.DocumentPath = document.DocumentPath;
        }

        // LimsDocuments rows need their identity DocumentId assigned before the
        // enriched summary (which embeds document_id) is persisted.
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var page in eligiblePages)
        {
            if (page.ReceiptDetailId.HasValue
                && documentsByReceipt.TryGetValue(page.ReceiptDetailId.Value, out var document))
            {
                page.DocumentId = document.DocumentId;
            }
        }

        await PersistEnrichedSummaryAsync(callbackId, summary, cancellationToken);
    }

    private async Task PersistEnrichedSummaryAsync(
        Guid callbackId,
        OcrCallbackInterfaceSummary summary,
        CancellationToken cancellationToken)
    {
        var enrichedJson = JsonSerializer.Serialize(summary);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE dbo.t_interface_lims_ocr_callback
            SET interface_summary_json = {enrichedJson}
            WHERE callback_id = {callbackId}
            """,
            cancellationToken);
    }

    // Mirrors QAQCPostFileToServer.ashx in WMS_NEW: attaching a COA document moves
    // the QAQC scan's COA status from NONE to PROCESSING for the document group.
    private async Task MarkScanCoaProcessingAsync(
        Guid receiptDetailId,
        string documentGroup,
        CancellationToken cancellationToken)
    {
        if (documentGroup is not ("QC_COA" or "QA_COA"))
        {
            return;
        }

        var scans = await dbContext.LimsQaqcScans
            .Where(x => x.ReceiptDetailId == receiptDetailId)
            .ToListAsync(cancellationToken);

        foreach (var scan in scans)
        {
            if (documentGroup == "QC_COA" && IsUnsetCoaStatus(scan.QcCoaStatus))
            {
                scan.QcCoaStatus = "PROCESSING";
            }
            else if (documentGroup == "QA_COA" && IsUnsetCoaStatus(scan.QaCoaStatus))
            {
                scan.QaCoaStatus = "PROCESSING";
            }
        }
    }

    private static bool IsUnsetCoaStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            || status.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase);
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
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath))
            + Path.DirectorySeparatorChar;
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

        if (fileName.Length > workerPrefixLength
            && fileName[17] == '_'
            && fileName[50] == '_')
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

    private static void DeleteCreatedDocuments(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Preserve the original database/interface exception.
            }
        }
    }
}
