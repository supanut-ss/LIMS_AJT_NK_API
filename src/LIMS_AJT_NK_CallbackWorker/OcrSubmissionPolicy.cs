using System.Net.Http.Json;
using System.Net.Http.Headers;
using LIMS_AJT_NK_CallbackWorker.Models;

namespace LIMS_AJT_NK_CallbackWorker;

public static class OcrSubmissionPolicy
{
    public const string PathSubmissionMode = "path";
    public const string FileSubmissionMode = "file";
    public const string InputOcrApiName = "input_ocr";
    public const string InputOcrFileApiName = "input_ocr_file";
    public const string DefaultFileFieldName = "files";

    public static string ResolveApiName(string? submissionMode)
    {
        var mode = string.IsNullOrWhiteSpace(submissionMode)
            ? PathSubmissionMode
            : submissionMode.Trim().ToLowerInvariant();

        return mode switch
        {
            PathSubmissionMode => InputOcrApiName,
            FileSubmissionMode => InputOcrFileApiName,
            _ => throw new InvalidOperationException(
                $"Unsupported OCR submission_mode '{submissionMode}'. Use '{PathSubmissionMode}' or '{FileSubmissionMode}'.")
        };
    }

    public static string ResolveEndpoint(LimsOcrConfigApiEntity config, string apiName)
    {
        var endpoint = apiName == InputOcrFileApiName
            ? config.InputOcrFileUrl
            : config.InputOcrUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException($"Endpoint URL for {apiName} is required.");
        }

        return endpoint.Trim();
    }

    public static string ResolveRequestUrl(
        LimsOcrConfigApiEntity config,
        string apiName,
        string? persistedRequestUrl)
    {
        return string.IsNullOrWhiteSpace(persistedRequestUrl)
            ? ResolveEndpoint(config, apiName)
            : persistedRequestUrl.Trim();
    }

    public static HttpContent CreateContent(
        string apiName,
        InputOcrRequest payload,
        string filePath,
        string? fileFieldName)
    {
        if (apiName == InputOcrApiName)
        {
            return JsonContent.Create(payload);
        }

        if (apiName != InputOcrFileApiName)
        {
            throw new InvalidOperationException($"Unsupported OCR API name '{apiName}'.");
        }

        if (string.IsNullOrWhiteSpace(payload.FlowId))
        {
            throw new InvalidOperationException("flow_id is required for the OCR file endpoint.");
        }

        if (string.IsNullOrWhiteSpace(payload.JobTaskId))
        {
            throw new InvalidOperationException("job_task_id is required for the OCR file endpoint.");
        }

        if (string.IsNullOrWhiteSpace(payload.CallbackUrl))
        {
            throw new InvalidOperationException("callback_url is required for the OCR file endpoint.");
        }

        var multipart = new MultipartFormDataContent();
        try
        {
            multipart.Add(new StringContent(payload.FlowId), "flow_id");
            multipart.Add(new StringContent(payload.JobTaskId), "job_task_id");
            multipart.Add(new StringContent(payload.CallbackUrl), "callback_url");

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            multipart.Add(
                fileContent,
                string.IsNullOrWhiteSpace(fileFieldName) ? DefaultFileFieldName : fileFieldName.Trim(),
                GetOriginalFileName(filePath));
            return multipart;
        }
        catch
        {
            multipart.Dispose();
            throw;
        }
    }

    public static HttpRequestMessage CreateRequest(
        string requestUrl,
        string apiName,
        InputOcrRequest payload,
        string filePath,
        string? fileFieldName,
        string? bearerToken)
    {
        if (apiName == InputOcrFileApiName && string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new InvalidOperationException(
                "input_ocr_bearer_token is required for the OCR file endpoint.");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        try
        {
            request.Content = CreateContent(apiName, payload, filePath, fileFieldName);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    bearerToken.Trim());
            }

            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    public static string ResolveAcceptedJobTaskId(
        string submittedJobTaskId,
        string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return submittedJobTaskId;
        }

        try
        {
            using var response = System.Text.Json.JsonDocument.Parse(responseBody);
            if (!response.RootElement.TryGetProperty("jobs", out var jobs)
                || jobs.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return submittedJobTaskId;
            }

            foreach (var job in jobs.EnumerateArray())
            {
                if (job.ValueKind == System.Text.Json.JsonValueKind.Object
                    && job.TryGetProperty("job_task_id", out var jobTaskId)
                    && jobTaskId.ValueKind == System.Text.Json.JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(jobTaskId.GetString()))
                {
                    return jobTaskId.GetString()!.Trim();
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Preserve compatibility with the legacy endpoint, whose successful
            // response does not contain the v5 jobs array.
        }

        return submittedJobTaskId;
    }

    public static string GetOriginalFileName(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        const int workerPrefixLength = 51;

        return fileName.Length > workerPrefixLength
            && fileName[17] == '_'
            && fileName[50] == '_'
                ? fileName[workerPrefixLength..]
                : fileName;
    }
}
