using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LIMS_AJT_NK_CallbackWorker.Models;

namespace LIMS_AJT_NK_CallbackWorker;

public static class OcrResultQueryPolicy
{
    public const string ApiName = "get_result_ocr";

    public static HttpRequestMessage CreateRequest(
        string endpointUrl,
        string bearerToken,
        string jobTaskId)
    {
        ValidateRequired(endpointUrl, "get_result_ocr_url");
        ValidateRequired(bearerToken, "input_ocr_bearer_token");
        ValidateRequired(jobTaskId, "job_task_id");

        var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl.Trim())
        {
            Content = JsonContent.Create(new AjiGetResultOcrRequest
            {
                JobTaskId = jobTaskId.Trim()
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            bearerToken.Trim());
        return request;
    }

    public static HttpRequestMessage CreateCallbackForwardRequest(
        string callbackUrl,
        string responseBody)
    {
        ValidateRequired(callbackUrl, "callback_url");
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new InvalidOperationException("OCR result response body is required.");
        }

        return new HttpRequestMessage(HttpMethod.Post, callbackUrl.Trim())
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
    }

    public static bool IsTerminalResponse(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using var response = JsonDocument.Parse(responseBody);
            if (!response.RootElement.TryGetProperty("summary", out var summary)
                || summary.ValueKind != JsonValueKind.Object
                || !summary.TryGetProperty("total", out var total)
                || !total.TryGetInt32(out var totalCount)
                || !summary.TryGetProperty("processing", out var processing)
                || !processing.TryGetInt32(out var processingCount))
            {
                return false;
            }

            return totalCount > 0 && processingCount == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ValidateRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }
    }
}
