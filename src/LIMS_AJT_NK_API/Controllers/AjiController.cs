using System.Text.Json;
using LIMS_AJT_NK_API.Models;
using LIMS_AJT_NK_API.Services;
using Microsoft.AspNetCore.Mvc;

namespace LIMS_AJT_NK_API.Controllers;

[ApiController]
[Route("api/aji")]
public class AjiController(IAjiApiClient ajiApiClient) : ControllerBase
{
    [HttpPost("get_result_ocr")]
    public async Task<IActionResult> GetResultOcr(
        [FromBody] AjiGetResultOcrRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.JobTaskId))
        {
            return BadRequest(CreateValidationError("job_task_id", "Value is required"));
        }

        return await ExecuteAsync(
            () => ajiApiClient.GetResultOcrAsync(request.JobTaskId, cancellationToken));
    }

    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback(
        [FromBody] AjiFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.JobTaskId))
        {
            return BadRequest(CreateValidationError("job_task_id", "Value is required"));
        }

        if (request.OcrResult.ValueKind != JsonValueKind.Object)
        {
            return BadRequest(CreateValidationError("ocr_result", "JSON object is required"));
        }

        return await ExecuteAsync(() => ajiApiClient.SendFeedbackAsync(
            request.JobTaskId,
            request.OcrResult,
            cancellationToken));
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<AjiApiCallResult>> action)
    {
        try
        {
            var result = await action();
            return new ContentResult
            {
                StatusCode = result.StatusCode,
                ContentType = result.ContentType,
                Content = result.ResponseBody
            };
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new
            {
                status = "error",
                message = ex.Message,
                errors = Array.Empty<object>()
            });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new
            {
                status = "error",
                message = "Unable to reach Aji API",
                errors = new[] { new { field = "upstream", message = ex.Message } }
            });
        }
    }

    private static object CreateValidationError(string field, string message)
    {
        return new
        {
            status = "error",
            message = "Invalid request",
            errors = new[] { new { field, message } }
        };
    }
}
