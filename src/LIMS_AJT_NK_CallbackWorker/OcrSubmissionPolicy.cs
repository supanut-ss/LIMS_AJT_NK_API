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

        var multipart = new MultipartFormDataContent();
        try
        {
            multipart.Add(new StringContent(payload.FlowId), "flow_id");
            multipart.Add(new StringContent(payload.JobTaskId), "job_task_id");
            if (!string.IsNullOrWhiteSpace(payload.CallbackUrl))
            {
                multipart.Add(new StringContent(payload.CallbackUrl), "callback_url");
            }

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            multipart.Add(
                fileContent,
                string.IsNullOrWhiteSpace(fileFieldName) ? "file" : fileFieldName.Trim(),
                GetOriginalFileName(filePath));
            return multipart;
        }
        catch
        {
            multipart.Dispose();
            throw;
        }
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
