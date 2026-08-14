using System.Globalization;
using System.Text.Json;
using LIMS_AJT_NK_API.Data;
using LIMS_AJT_NK_API.Models;
using LIMS_AJT_NK_API.Services;
using Microsoft.AspNetCore.Mvc;

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
        var callback = BuildCallback(request, normalizedJobTaskId, now);

        dbContext.InterfaceLimsOcrCallbacks.Add(callback);
        await dbContext.SaveChangesAsync(cancellationToken);

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
                    job_task_id = normalizedJobTaskId,
                    result_count = callback.Results.Count,
                    item_count = callback.Results.Sum(x => x.Items.Count),
                    interface_status = "error"
                },
                errors = new[] { new { field = "interface", message = "Unable to update QAQC results" } }
            };

            await WriteCallbackLogAsync(
                request,
                normalizedJobTaskId,
                errorResponse,
                500,
                "callback_error",
                false,
                ex.Message,
                cancellationToken);

            return StatusCode(500, errorResponse);
        }

        var response = new
        {
            status = "success",
            message = "Callback received and processed",
            data = new
            {
                callback_id = callback.CallbackId,
                job_task_id = normalizedJobTaskId,
                result_count = callback.Results.Count,
                item_count = callback.Results.Sum(x => x.Items.Count),
                interface_status = interfaceSummary.InterfaceStatus,
                updated_item_count = interfaceSummary.UpdatedItemCount,
                skipped_item_count = interfaceSummary.SkippedItemCount,
                pages = interfaceSummary.Pages
            },
            errors = (object?)null
        };

        var workStatus = interfaceSummary.InterfaceStatus switch
        {
            OcrInterfaceStatuses.Completed => "callback_processed",
            OcrInterfaceStatuses.Partial => "callback_partial",
            _ => "callback_not_matched"
        };

        await WriteCallbackLogAsync(
            request,
            normalizedJobTaskId,
            response,
            200,
            workStatus,
            interfaceSummary.IsCompleted,
            interfaceSummary.IsCompleted ? null : interfaceSummary.InterfaceStatus,
            cancellationToken);

        return Ok(response);
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
        DateTime now)
    {
        var callback = new InterfaceLimsOcrCallbackEntity
        {
            CallbackId = Guid.NewGuid(),
            JobTaskId = jobTaskId,
            IsInterface = false,
            CreateBy = "api",
            CreateDate = now
        };

        foreach (var page in request.OcrResult)
        {
            var body = page.BodyJson;
            var result = new InterfaceLimsOcrResultEntity
            {
                ResultId = Guid.NewGuid(),
                Callback = callback,
                IsInterface = false,
                PageId = page.PageId,
                TrackingId = page.TrackingId,
                TrackingStatus = page.TrackingStatus!.Trim(),
                ProductName = body?.ProductName,
                DocumentType = body?.DocumentType,
                SupplierName = body?.SupplierName,
                LotNumber = body?.LotNumber,
                OriginSupplierName = body?.OriginSupplierName,
                OriginProductName = body?.OriginProductName,
                ExpiryDate = ParseDate(body?.ExpiryDate),
                MfgDate = ParseDate(body?.MfgDate),
                InternalLot = body?.InternalLot,
                Quantity = ParseDecimal(body?.Quantity),
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
                        ResultValue = item.Result,
                        Uom = item.Uom,
                        CreateBy = "api",
                        CreateDate = now
                    });
                }
            }

            callback.Results.Add(result);
        }

        return callback;
    }

    private static (string Field, string Message)? ValidateResults(IEnumerable<OcrResultRequest> results)
    {
        foreach (var page in results)
        {
            if (page.PageId <= 0)
            {
                return ("page_id", "Value must be greater than zero");
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

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
            ? quantity
            : null;
    }
}
