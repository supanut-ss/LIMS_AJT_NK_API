using System.Net.Http.Headers;
using System.Text.Json;

namespace LIMS_AJT_NK_CallbackWorker;

public static class MasterDataSubmissionPolicy
{
    public const string ApiName = "update_master_data";
    public const string DefaultFileFieldName = "files";
    public const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static string ResolveEndpoint(string? endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            throw new InvalidOperationException("update_master_url is required.");
        }

        return endpointUrl.Trim();
    }

    public static HttpRequestMessage CreateRequest(
        string requestUrl,
        string flowId,
        string filePath)
    {
        if (string.IsNullOrWhiteSpace(flowId))
        {
            throw new InvalidOperationException("flow_id is required for update_master_data.");
        }

        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("update_master_data accepts only .xlsx files.");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        var multipart = new MultipartFormDataContent();
        try
        {
            multipart.Add(new StringContent(flowId.Trim()), "flow_id");

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(ExcelContentType);
            multipart.Add(
                fileContent,
                DefaultFileFieldName,
                OcrSubmissionPolicy.GetOriginalFileName(filePath));

            request.Content = multipart;
            return request;
        }
        catch
        {
            multipart.Dispose();
            request.Dispose();
            throw;
        }
    }

    public static bool IsSuccessfulResponse(string? responseBody)
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
                && string.Equals(
                    status.GetString()?.Trim(),
                    "success",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
