using LIMS_AJT_NK_API.Data;
using LIMS_AJT_NK_API.Models;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_API.Services;

public interface IOcrCallbackInterfaceService
{
    Task<OcrCallbackInterfaceSummary> ProcessAsync(Guid callbackId, CancellationToken cancellationToken = default);
}

public sealed class OcrCallbackInterfaceService(
    ApplicationDbContext dbContext,
    ILogger<OcrCallbackInterfaceService> logger) : IOcrCallbackInterfaceService
{
    private const string ActiveValue = "YES";
    private const string InterfaceUser = "api";
    private const int ResultMaxLength = 50;

    public async Task<OcrCallbackInterfaceSummary> ProcessAsync(
        Guid callbackId,
        CancellationToken cancellationToken = default)
    {
        var callback = await dbContext.InterfaceLimsOcrCallbacks
            .Include(x => x.Results)
            .ThenInclude(x => x.Items)
            .SingleOrDefaultAsync(x => x.CallbackId == callbackId, cancellationToken)
            ?? throw new InvalidOperationException($"OCR callback '{callbackId}' was not found.");

        ResetInterfaceFlags(callback);
        var createdDocumentPaths = new List<string>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var summary = new OcrCallbackInterfaceSummary();
            var hasEligibleResults = callback.Results.Any(IsReadyToCheck);

            foreach (var result in callback.Results.OrderBy(x => x.PageId))
            {
                if (!IsReadyToCheck(result))
                {
                    summary.Pages.Add(new OcrCallbackInterfacePageResult
                    {
                        PageId = result.PageId,
                        Status = "ignored",
                        Reason = "tracking_status_not_ready"
                    });
                    continue;
                }

                summary.Pages.Add(await ProcessResultAsync(result, cancellationToken));
            }

            summary.UpdatedItemCount = summary.Pages.Sum(x => x.UpdatedItemCount);
            summary.SkippedItemCount = summary.Pages.Sum(x => x.SkippedItemCount);

            var completed = hasEligibleResults
                && summary.Pages
                    .Where(x => x.Status != "ignored")
                    .All(x => x.Status == "completed");

            summary.InterfaceStatus = completed
                ? OcrInterfaceStatuses.Completed
                : summary.UpdatedItemCount == 0
                    ? OcrInterfaceStatuses.NotMatched
                    : OcrInterfaceStatuses.Partial;

            var documentBindings = await StoreDocumentsAsync(
                callback,
                summary.Pages,
                createdDocumentPaths,
                cancellationToken);

            callback.IsInterface = completed;
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var binding in documentBindings)
            {
                binding.Page.DocumentId = binding.Document.DocumentId;
            }

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

    private async Task<List<DocumentBinding>> StoreDocumentsAsync(
        InterfaceLimsOcrCallbackEntity callback,
        IReadOnlyCollection<OcrCallbackInterfacePageResult> pages,
        ICollection<string> createdDocumentPaths,
        CancellationToken cancellationToken)
    {
        var eligiblePages = pages
            .Where(x => x.ReceiptDetailId.HasValue && x.Status != "ignored")
            .ToList();

        if (eligiblePages.Count == 0)
        {
            return [];
        }

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

            return [];
        }

        var documentGroup = string.IsNullOrWhiteSpace(config.DocumentGroup)
            ? "QC_COA"
            : config.DocumentGroup.Trim();
        ValidateDocumentGroup(documentGroup);

        var sourceLog = await dbContext.InterfaceLimsOcrLogs
            .AsNoTracking()
            .Where(x => x.ApiName == "input_ocr"
                && x.JobTaskId == callback.JobTaskId
                && x.FilePath != null)
            .OrderByDescending(x => x.CreateDate)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"Input OCR file log was not found for job_task_id '{callback.JobTaskId}'.");

        var sourcePath = Path.GetFullPath(sourceLog.FilePath!);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Input OCR source file was not found.", sourcePath);
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
        var bindings = new List<DocumentBinding>();
        var documentsByReceipt = new Dictionary<Guid, LimsDocumentEntity>();

        foreach (var page in eligiblePages)
        {
            var receiptDetailId = page.ReceiptDetailId!.Value;
            if (documentsByReceipt.TryGetValue(receiptDetailId, out var currentDocument))
            {
                page.DocumentStatus = "stored";
                page.DocumentPath = currentDocument.DocumentPath;
                bindings.Add(new DocumentBinding(page, currentDocument));
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

            var result = callback.Results.Single(x => x.PageId == page.PageId);
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
                Udf2 = callback.CallbackId.ToString(),
                Udf3 = result.FileId ?? result.TrackingId,
                IsActive = true,
                CreateBy = InterfaceUser,
                CreateDate = DateTime.Now
            };

            dbContext.LimsDocuments.Add(document);
            documentsByReceipt[receiptDetailId] = document;
            page.DocumentStatus = "stored";
            page.DocumentPath = document.DocumentPath;
            bindings.Add(new DocumentBinding(page, document));
        }

        return bindings;
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

    private sealed record DocumentBinding(
        OcrCallbackInterfacePageResult Page,
        LimsDocumentEntity Document);

    private async Task<OcrCallbackInterfacePageResult> ProcessResultAsync(
        InterfaceLimsOcrResultEntity result,
        CancellationToken cancellationToken)
    {
        var page = new OcrCallbackInterfacePageResult { PageId = result.PageId };
        var matchDataError = GetMatchDataError(result);
        if (matchDataError is not null)
        {
            return FailPage(page, result.Items, matchDataError);
        }

        var productName = result.ProductName!.Trim();
        var supplierName = result.SupplierName!.Trim();
        var lotNumber = !string.IsNullOrWhiteSpace(result.LotNumber)
            ? result.LotNumber.Trim()
            : result.InternalLot!.Trim();
        var mfgDate = result.MfgDate!.Value.Date;

        var inboundCandidates = await (
                from inboundRow in dbContext.LimsInboundReceives.AsNoTracking()
                join item in dbContext.WmsItems.AsNoTracking()
                    on inboundRow.ItemMasterId equals (Guid?)item.ItemMasterId
                where item.Description != null
                    && inboundRow.SupplierName != null
                    && inboundRow.LotNumber != null
                    && item.Description.Trim() == productName
                    && inboundRow.SupplierName.Trim() == supplierName
                    && inboundRow.LotNumber.Trim() == lotNumber
                    && inboundRow.MfgDate == mfgDate
                select inboundRow)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (inboundCandidates.Count == 0)
        {
            return FailPage(page, result.Items, "inbound_not_found");
        }

        if (inboundCandidates.Count > 1)
        {
            return FailPage(page, result.Items, "inbound_ambiguous");
        }

        var inbound = inboundCandidates[0];
        page.ReceiptDetailId = inbound.ReceiptDetailId;

        var scanCount = await dbContext.LimsQaqcScans
            .AsNoTracking()
            .Where(x => x.ReceiptDetailId == inbound.ReceiptDetailId)
            .Take(2)
            .CountAsync(cancellationToken);

        if (scanCount == 0)
        {
            return FailPage(page, result.Items, "qaqc_scan_not_found");
        }

        if (scanCount > 1)
        {
            return FailPage(page, result.Items, "qaqc_scan_ambiguous");
        }

        if (result.Items.Count == 0)
        {
            page.Status = "not_matched";
            page.Reason = "parameter_items_missing";
            result.IsInterface = false;
            return page;
        }

        foreach (var item in result.Items.OrderBy(x => x.Seq))
        {
            var itemResult = await ProcessItemAsync(item, inbound, cancellationToken);
            page.Items.Add(itemResult);

            if (itemResult.Status == "updated")
            {
                page.UpdatedItemCount++;
            }
            else
            {
                page.SkippedItemCount++;
            }
        }

        var pageCompleted = page.UpdatedItemCount > 0 && page.SkippedItemCount == 0;
        page.Status = pageCompleted ? "completed" : page.UpdatedItemCount == 0 ? "not_matched" : "partial";
        result.IsInterface = pageCompleted;

        return page;
    }

    private async Task<OcrCallbackInterfaceItemResult> ProcessItemAsync(
        InterfaceLimsOcrResultItemEntity item,
        LimsInboundReceiveEntity inbound,
        CancellationToken cancellationToken)
    {
        item.IsInterface = false;

        var response = new OcrCallbackInterfaceItemResult
        {
            Seq = item.Seq,
            ParameterName = item.ParameterName,
            Status = "skipped"
        };

        if (string.IsNullOrWhiteSpace(item.ResultValue))
        {
            response.Reason = "result_missing";
            return response;
        }

        var normalizedResult = item.ResultValue.Trim();
        if (normalizedResult.Length > ResultMaxLength)
        {
            response.Reason = "result_too_long";
            return response;
        }

        var normalizedParameterName = item.ParameterName.Trim();
        var mappings = await dbContext.LimsCoaParameterMappings
            .AsNoTracking()
            .Where(x => x.IsActive.Trim() == ActiveValue
                && x.OcrParameterName != null
                && x.CoaParameterName != null
                && x.OcrParameterName.Trim() == normalizedParameterName)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (mappings.Count == 0)
        {
            response.Reason = "mapping_not_found";
            return response;
        }

        if (mappings.Count > 1)
        {
            response.Reason = "mapping_ambiguous";
            return response;
        }

        var coaParameterName = mappings[0].CoaParameterName!.Trim();
        var coaParameters = await dbContext.LimsCoaParameters
            .AsNoTracking()
            .Where(x => x.IsActive.Trim() == ActiveValue
                && x.CoaParameterName != null
                && x.CoaParameterName.Trim() == coaParameterName)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (coaParameters.Count == 0)
        {
            response.Reason = "coa_parameter_not_found";
            return response;
        }

        if (coaParameters.Count > 1)
        {
            response.Reason = "coa_parameter_ambiguous";
            return response;
        }

        var coaParameterId = coaParameters[0].CoaParameterId;
        var targets = await (
                from manage in dbContext.LimsManageCoaParameters
                join test in dbContext.LimsQaqcCoaParameterTests
                    on manage.ManageCoaParameterId equals test.ManageCoaParameterId
                where manage.CoaParameterId == coaParameterId
                    && manage.IsActive.Trim() == ActiveValue
                    && test.ReceiptDetailId == inbound.ReceiptDetailId
                select new { manage.ManageCoaParameterId, Test = test })
            .Take(2)
            .ToListAsync(cancellationToken);

        if (targets.Count == 0)
        {
            response.Reason = "parameter_test_not_found";
            return response;
        }

        if (targets.Count > 1)
        {
            response.Reason = "parameter_test_ambiguous";
            return response;
        }

        var target = targets[0];
        var now = DateTime.Now;
        target.Test.Result = normalizedResult;
        target.Test.UpdateBy = InterfaceUser;
        target.Test.UpdateDate = now;

        dbContext.LimsQaqcCoaParameterTransactions.Add(new LimsQaqcCoaParameterTransactionEntity
        {
            TranId = Guid.NewGuid(),
            LogType = "OCR Callback",
            ReceiptDetailId = inbound.ReceiptDetailId,
            ManageCoaParameterId = target.ManageCoaParameterId,
            Result = normalizedResult,
            ResultBetween = target.Test.ResultBetween,
            ReceiveDate = inbound.ReceiveDate,
            UomId = inbound.UomId,
            TransationBy = InterfaceUser,
            TransactionDate = now
        });

        item.IsInterface = true;
        response.Status = "updated";
        return response;
    }

    private static bool IsReadyToCheck(InterfaceLimsOcrResultEntity result)
    {
        return string.Equals(
            result.TrackingStatus?.Trim(),
            "ReadyToCheck",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void ResetInterfaceFlags(InterfaceLimsOcrCallbackEntity callback)
    {
        callback.IsInterface = false;

        foreach (var result in callback.Results)
        {
            result.IsInterface = false;

            foreach (var item in result.Items)
            {
                item.IsInterface = false;
            }
        }
    }

    private static string? GetMatchDataError(InterfaceLimsOcrResultEntity result)
    {
        if (string.IsNullOrWhiteSpace(result.ProductName))
        {
            return "product_name_missing";
        }

        if (string.IsNullOrWhiteSpace(result.SupplierName))
        {
            return "supplier_name_missing";
        }

        if (string.IsNullOrWhiteSpace(result.LotNumber) && string.IsNullOrWhiteSpace(result.InternalLot))
        {
            return "lot_number_missing";
        }

        return result.MfgDate is null ? "mfg_date_missing" : null;
    }

    private static OcrCallbackInterfacePageResult FailPage(
        OcrCallbackInterfacePageResult page,
        IEnumerable<InterfaceLimsOcrResultItemEntity> items,
        string reason)
    {
        page.Status = "not_matched";
        page.Reason = reason;

        foreach (var item in items.OrderBy(x => x.Seq))
        {
            page.Items.Add(new OcrCallbackInterfaceItemResult
            {
                Seq = item.Seq,
                ParameterName = item.ParameterName,
                Status = "skipped",
                Reason = reason
            });
            page.SkippedItemCount++;
        }

        return page;
    }
}
