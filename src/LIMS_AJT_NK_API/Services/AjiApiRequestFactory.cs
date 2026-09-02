using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LIMS_AJT_NK_API.Models;

namespace LIMS_AJT_NK_API.Services;

public static class AjiApiRequestFactory
{
    public static HttpRequestMessage CreateGetResultOcrRequest(
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

    public static HttpRequestMessage CreateFeedbackRequest(
        string endpointUrl,
        string jobTaskId,
        JsonElement ocrResult)
    {
        ValidateRequired(endpointUrl, "feedback_url");
        ValidateRequired(jobTaskId, "job_task_id");
        if (ocrResult.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("ocr_result must be a JSON object.");
        }

        return new HttpRequestMessage(HttpMethod.Post, endpointUrl.Trim())
        {
            Content = JsonContent.Create(new AjiFeedbackRequest
            {
                JobTaskId = jobTaskId.Trim(),
                OcrResult = ocrResult
            })
        };
    }

    public static bool IsSuccessStatus(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using var response = JsonDocument.Parse(responseBody);
            return response.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString()?.Trim(), "success", StringComparison.OrdinalIgnoreCase);
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
