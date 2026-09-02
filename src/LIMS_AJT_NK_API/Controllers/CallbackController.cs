using System.Buffers;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LIMS_AJT_NK_API.Data;
using LIMS_AJT_NK_API.Models;
using LIMS_AJT_NK_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_API.Controllers;

[ApiController]
[Route("api")]
public class CallbackController(
    ApplicationDbContext dbContext,
    IOcrCallbackInterfaceService interfaceService,
    ILogger<CallbackController> logger) : ControllerBase
{
    [HttpGet("test")]
    public IActionResult TestApi()
    {
        return Ok(new
        {
            status = "success",
            message = "API is reachable",
            data = new { server_time = DateTime.Now },
            errors = (object?)null
        });
    }

    [HttpGet("health")]
    public async Task<IActionResult> HealthCheck(CancellationToken cancellationToken)
    {
        var isDbHealthy = false;
        string? dbError = null;
        try
        {
            isDbHealthy = await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            dbError = ex.Message;
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        var assembly = typeof(Program).Assembly;
        var version = System.Reflection.CustomAttributeExtensions
                          .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)
                          ?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? "1.0.0";

        var response = new
        {
            status = isDbHealthy ? "healthy" : "unhealthy",
            message = isDbHealthy ? "Service is running and database is connected" : "Service is degraded",
            data = new
            {
                version,
                uptime = $"{(int)uptime.TotalDays}d {uptime.Hours:D2}h {uptime.Minutes:D2}m {uptime.Seconds:D2}s",
                uptime_seconds = uptime.TotalSeconds,
                db_status = isDbHealthy ? "connected" : "disconnected",
                db_error = dbError,
                server_time_utc = DateTime.UtcNow
            },
            errors = isDbHealthy
                ? null
                : new[] { new { field = "database", message = dbError ?? "Unable to connect to database" } }
        };

        return isDbHealthy ? Ok(response) : StatusCode(503, response);
    }

    [HttpPost("~/callback_test")]
    public async Task<IActionResult> CallbackTest(
        [FromBody] JsonElement payload,
        CancellationToken cancellationToken)
    {
        var requestPayload = payload.GetRawText();
        string? jobTaskId = null;
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("job_task_id", out var jobTaskIdElement)
            && jobTaskIdElement.ValueKind == JsonValueKind.String)
        {
            jobTaskId = jobTaskIdElement.GetString()?.Trim();
        }

        dbContext.InterfaceLimsOcrLogs.Add(new InterfaceLimsOcrLogEntity
        {
            LogId = Guid.NewGuid(),
            ApiName = "callback_test",
            RequestUrl = HttpContext.Request.Path,
            JobTaskId = jobTaskId,
            RequestPayload = requestPayload,
            ResponseStatusCode = StatusCodes.Status200OK,
            ResponsePayload = "{\"status\":\"success\"}",
            IsSuccess = true,
            SourceSystem = "callback_test",
            WorkStatus = "completed_success",
            AttemptCount = 1,
            CompletedDate = DateTime.Now,
            IsInterface = false,
            CreateBy = "api",
            CreateDate = DateTime.Now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "callback_test received. JobTaskId={JobTaskId}, PayloadLength={PayloadLength}",
            jobTaskId,
            requestPayload.Length);

        return Ok(new
        {
            status = "success",
            message = "Callback payload received",
            data = new { job_task_id = jobTaskId },
            errors = (object?)null
        });
    }

    [HttpPost("call_back")]
    public async Task<IActionResult> ReceiveOcrResult(
        [FromBody] OcrCallbackRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.JobTaskId))
        {
            return ValidationError("job_task_id", "Value is required");
        }

        if (request.OcrResult.Count == 0)
        {
            return ValidationError("ocr_result", "At least one result is required");
        }

        var validationError = ValidateResults(request.OcrResult);
        if (validationError is not null)
        {
            return ValidationError(validationError.Value.Field, validationError.Value.Message);
        }

        var now = DateTime.Now;
        var normalizedJobTaskId = request.JobTaskId.Trim();
        // job_task_id is an opaque producer ID. Only surrounding whitespace is
        // normalized; case remains significant and the column uses BIN2 collation.
        var idempotencyKey = normalizedJobTaskId;
        request.JobTaskId = normalizedJobTaskId;
        var candidate = BuildCallback(request, normalizedJobTaskId, idempotencyKey, now);
        var requestHash = ComputeRequestHash(candidate);
        candidate.RequestHash = requestHash;

        var (callback, isDuplicate) = await GetOrCreateCallbackAsync(
            candidate,
            idempotencyKey,
            cancellationToken);

        if (!string.Equals(callback.RequestHash, requestHash, StringComparison.Ordinal))
        {
            var conflictResponse = new
            {
                status = "error",
                message = "job_task_id was already used with a different callback payload",
                data = new
                {
                    callback_id = callback.CallbackId,
                    job_task_id = callback.JobTaskId,
                    interface_status = callback.InterfaceStatus
                },
                errors = new[]
                {
                    new
                    {
                        field = "job_task_id",
                        message = "Use a new job_task_id when the OCR payload changes"
                    }
                }
            };

            await WriteCallbackLogAsync(
                request,
                callback.JobTaskId,
                conflictResponse,
                409,
                "callback_conflict",
                false,
                "idempotency_payload_conflict",
                cancellationToken);

            return Conflict(conflictResponse);
        }

        OcrCallbackInterfaceSummary interfaceSummary;
        try
        {
            interfaceSummary = await interfaceService.ProcessAsync(callback.CallbackId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to interface OCR callback {CallbackId} to QAQC.", callback.CallbackId);

            var errorResponse = new
            {
                status = "error",
                message = "Callback was saved, but QAQC interface processing failed",
                data = new
                {
                    callback_id = callback.CallbackId,
                    job_task_id = callback.JobTaskId,
                    result_count = callback.Results.Count,
                    item_count = callback.Results.Sum(x => x.Items.Count),
                    interface_status = "error"
                },
                errors = new[] { new { field = "interface", message = "Unable to update QAQC results" } }
            };

            await WriteCallbackLogAsync(
                request,
                callback.JobTaskId,
                errorResponse,
                500,
                "callback_error",
                false,
                ex.Message,
                cancellationToken);

            return StatusCode(500, errorResponse);
        }

        var idempotencyStatus = !isDuplicate
            ? "accepted"
            : interfaceSummary.IsReplay
                ? "replayed"
                : "processed_existing";
        var response = new
        {
            status = "success",
            message = idempotencyStatus switch
            {
                "replayed" => "Duplicate callback received; the original result was replayed",
                "processed_existing" => "Existing pending callback was processed",
                _ => "Callback received and processed"
            },
            data = new
            {
                callback_id = callback.CallbackId,
                job_task_id = callback.JobTaskId,
                is_duplicate = isDuplicate,
                idempotency_status = idempotencyStatus,
                result_count = callback.Results.Count,
                item_count = callback.Results.Sum(x => x.Items.Count),
                interface_status = interfaceSummary.InterfaceStatus,
                updated_item_count = interfaceSummary.UpdatedItemCount,
                skipped_item_count = interfaceSummary.SkippedItemCount,
                already_processed_item_count = interfaceSummary.AlreadyProcessedItemCount,
                pages = interfaceSummary.Pages
            },
            errors = (object?)null
        };

        var workStatus = idempotencyStatus switch
        {
            "replayed" => "callback_replayed",
            "processed_existing" => "callback_processed_existing",
            _ => interfaceSummary.InterfaceStatus switch
            {
                OcrInterfaceStatuses.Completed => "callback_processed",
                OcrInterfaceStatuses.Partial => "callback_partial",
                _ => "callback_not_matched"
            }
        };

        await WriteCallbackLogAsync(
            request,
            callback.JobTaskId,
            response,
            200,
            workStatus,
            interfaceSummary.IsCompleted,
            interfaceSummary.IsCompleted ? null : interfaceSummary.InterfaceStatus,
            cancellationToken);

        return Ok(response);
    }

    private async Task<(InterfaceLimsOcrCallbackEntity Callback, bool IsDuplicate)> GetOrCreateCallbackAsync(
        InterfaceLimsOcrCallbackEntity callback,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            var existing = await dbContext.InterfaceLimsOcrCallbacks
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM dbo.t_interface_lims_ocr_callback WITH
                        (UPDLOCK, HOLDLOCK, INDEX(uq_ocr_callback_idempotency_key))
                    WHERE idempotency_key = {idempotencyKey}
                    """)
                .Include(x => x.Results)
                .ThenInclude(x => x.Items)
                .SingleOrDefaultAsync(cancellationToken);

            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return (existing, true);
            }

            dbContext.InterfaceLimsOcrCallbacks.Add(callback);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (callback, false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DetachCallbackGraph(callback);

            var existing = await LoadCallbackByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Idempotent callback '{idempotencyKey}' was not found after a unique-key conflict.",
                    ex);
            return (existing, true);
        }
    }

    private Task<InterfaceLimsOcrCallbackEntity?> LoadCallbackByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return dbContext.InterfaceLimsOcrCallbacks
            .Include(x => x.Results)
            .ThenInclude(x => x.Items)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }

    private void DetachCallbackGraph(InterfaceLimsOcrCallbackEntity callback)
    {
        foreach (var item in callback.Results.SelectMany(x => x.Items))
        {
            dbContext.Entry(item).State = EntityState.Detached;
        }

        foreach (var result in callback.Results)
        {
            dbContext.Entry(result).State = EntityState.Detached;
        }

        dbContext.Entry(callback).State = EntityState.Detached;
    }

    private async Task WriteCallbackLogAsync(
        OcrCallbackRequest request,
        string jobTaskId,
        object response,
        int responseStatusCode,
        string workStatus,
        bool isInterface,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        dbContext.InterfaceLimsOcrLogs.Add(new InterfaceLimsOcrLogEntity
        {
            LogId = Guid.NewGuid(),
            ApiName = "call_back",
            RequestUrl = "/api/call_back",
            JobTaskId = jobTaskId,
            RequestPayload = JsonSerializer.Serialize(request),
            ResponseStatusCode = responseStatusCode,
            ResponsePayload = JsonSerializer.Serialize(response),
            IsSuccess = isInterface,
            ErrorMessage = errorMessage,
            SourceSystem = "api",
            WorkStatus = workStatus,
            CompletedDate = DateTime.Now,
            IsInterface = isInterface,
            CreateBy = "api",
            CreateDate = DateTime.Now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static InterfaceLimsOcrCallbackEntity BuildCallback(
        OcrCallbackRequest request,
        string jobTaskId,
        string idempotencyKey,
        DateTime now)
    {
        var callback = new InterfaceLimsOcrCallbackEntity
        {
            CallbackId = Guid.NewGuid(),
            JobTaskId = jobTaskId,
            IdempotencyKey = idempotencyKey,
            InterfaceStatus = OcrInterfaceStatuses.Pending,
            SourceSummaryJson = request.Summary is null
                ? null
                : CanonicalizeJson(JsonSerializer.Serialize(request.Summary)),
            IsInterface = false,
            CreateBy = "api",
            CreateDate = now
        };

        var assignedPageIds = request.OcrResult
            .Where(x => x.PageId.GetValueOrDefault() > 0)
            .Select(x => x.PageId!.Value)
            .ToHashSet();
        var nextGeneratedPageId = 1;

        for (var pageIndex = 0; pageIndex < request.OcrResult.Count; pageIndex++)
        {
            var page = request.OcrResult[pageIndex];
            var body = page.BodyJson;
            var quantity = ParseQuantity(body?.Quantity);
            var storedPageId = page.PageId.GetValueOrDefault();
            if (storedPageId <= 0)
            {
                while (assignedPageIds.Contains(nextGeneratedPageId))
                {
                    nextGeneratedPageId++;
                }

                storedPageId = nextGeneratedPageId++;
                assignedPageIds.Add(storedPageId);
            }

            var result = new InterfaceLimsOcrResultEntity
            {
                ResultId = Guid.NewGuid(),
                Callback = callback,
                IsInterface = false,
                PageId = storedPageId,
                FileId = NormalizeOptional(page.FileId),
                TrackingId = NormalizeOptional(page.TrackingId),
                TrackingStatus = page.TrackingStatus!.Trim(),
                Status = NormalizeOptional(page.Status),
                ProductName = NormalizeOptional(body?.ProductName),
                DocumentType = NormalizeOptional(body?.DocumentType),
                DocumentClassification = NormalizeOptional(body?.DocumentClassification),
                SupplierName = NormalizeOptional(body?.SupplierName),
                LotNumber = FirstNotEmpty(body?.LotNumber, body?.InternalLot),
                OriginSupplierName = NormalizeOptional(body?.OriginSupplierName),
                OriginProductName = NormalizeOptional(body?.OriginProductName),
                ExpiryDate = ParseDate(body?.ExpiryDate),
                MfgDate = ParseDate(body?.MfgDate),
                InternalLot = NormalizeOptional(body?.InternalLot),
                Quantity = quantity.Value,
                QuantityUom = quantity.Uom,
                ConfidenceJson = page.Confident.HasValue
                    ? CanonicalizeJson(page.Confident.Value.GetRawText())
                    : null,
                CreateBy = "api",
                CreateDate = now
            };

            if (body is not null)
            {
                for (var index = 0; index < body.BodyItem.Count; index++)
                {
                    var item = body.BodyItem[index];
                    result.Items.Add(new InterfaceLimsOcrResultItemEntity
                    {
                        ItemId = Guid.NewGuid(),
                        OcrResult = result,
                        IsInterface = false,
                        Seq = index + 1,
                        ParameterName = item.ParameterName!.Trim(),
                        ResultValue = NormalizeOptional(item.Result),
                        Uom = NormalizeOptional(item.Uom),
                        CreateBy = "api",
                        CreateDate = now
                    });
                }
            }

            callback.Results.Add(result);
        }

        return callback;
    }

    private static string ComputeRequestHash(InterfaceLimsOcrCallbackEntity callback)
    {
        var canonicalPayload = new
        {
            job_task_id = callback.IdempotencyKey,
            source_summary = CanonicalizeJson(callback.SourceSummaryJson),
            ocr_result = callback.Results
                .OrderBy(x => x.PageId)
                .Select(x => new
                {
                    page_id = x.PageId,
                    file_id = x.FileId,
                    tracking_id = NormalizeOptional(x.TrackingId),
                    tracking_status = x.TrackingStatus.Trim(),
                    status = NormalizeOptional(x.Status),
                    product_name = NormalizeOptional(x.ProductName),
                    document_type = NormalizeOptional(x.DocumentType),
                    document_classification = NormalizeOptional(x.DocumentClassification),
                    supplier_name = NormalizeOptional(x.SupplierName),
                    lot_number = NormalizeOptional(x.LotNumber),
                    origin_supplier_name = NormalizeOptional(x.OriginSupplierName),
                    origin_product_name = NormalizeOptional(x.OriginProductName),
                    expiry_date = x.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    mfg_date = x.MfgDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    internal_lot = NormalizeOptional(x.InternalLot),
                    quantity = x.Quantity,
                    quantity_uom = x.QuantityUom,
                    confident = CanonicalizeJson(x.ConfidenceJson),
                    body_item = x.Items
                        .OrderBy(item => item.Seq)
                        .Select(item => new
                        {
                            seq = item.Seq,
                            parameter_name = item.ParameterName.Trim(),
                            result = NormalizeOptional(item.ResultValue),
                            uom = NormalizeOptional(item.Uom)
                        })
                })
        };

        var json = JsonSerializer.Serialize(canonicalPayload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string? CanonicalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonicalJson(writer, document.RootElement);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonicalJson(writer, property.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
            {
                WriteCanonicalJson(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        element.WriteTo(writer);
    }

    private static (string Field, string Message)? ValidateResults(IEnumerable<OcrResultRequest> results)
    {
        var pageIds = new HashSet<int>();
        foreach (var page in results)
        {
            if (page.PageId.GetValueOrDefault() <= 0 && string.IsNullOrWhiteSpace(page.FileId))
            {
                return ("page_id/file_id", "Either page_id greater than zero or file_id is required");
            }

            if (page.PageId.GetValueOrDefault() > 0 && !pageIds.Add(page.PageId!.Value))
            {
                return ("page_id", "Value must be unique within ocr_result");
            }

            if (string.IsNullOrWhiteSpace(page.TrackingStatus))
            {
                return ("tracking_status", "Value is required");
            }

            if (page.BodyJson?.BodyItem.Any(x => string.IsNullOrWhiteSpace(x.ParameterName)) == true)
            {
                return ("parameter_name", "Value is required");
            }
        }

        return null;
    }

    private BadRequestObjectResult ValidationError(string field, string message)
    {
        return BadRequest(new
        {
            status = "error",
            message = "Invalid request data",
            data = new { },
            errors = new[] { new { field, message } }
        });
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            "dd/MM/yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static (decimal? Value, string? Uom) ParseQuantity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var match = Regex.Match(
            value,
            @"^\s*(?<number>[+-]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)\s*(?<uom>.*?)\s*$",
            RegexOptions.CultureInvariant);

        if (!match.Success ||
            !decimal.TryParse(
                match.Groups["number"].Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var quantity))
        {
            return (null, null);
        }

        return (quantity, NormalizeOptional(match.Groups["uom"].Value));
    }

    private static string? FirstNotEmpty(string? primary, string? fallback)
    {
        return NormalizeOptional(primary) ?? NormalizeOptional(fallback);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
