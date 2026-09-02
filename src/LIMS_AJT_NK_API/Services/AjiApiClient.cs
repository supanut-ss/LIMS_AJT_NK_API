using System.Text.Json;
using LIMS_AJT_NK_API.Data;
using LIMS_AJT_NK_API.Models;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_API.Services;

public interface IAjiApiClient
{
    Task<AjiApiCallResult> GetResultOcrAsync(
        string jobTaskId,
        CancellationToken cancellationToken);

    Task<AjiApiCallResult> SendFeedbackAsync(
        string jobTaskId,
        JsonElement ocrResult,
        CancellationToken cancellationToken);
}

public class AjiApiClient(
    ApplicationDbContext dbContext,
    IHttpClientFactory httpClientFactory) : IAjiApiClient
{
    public async Task<AjiApiCallResult> GetResultOcrAsync(
        string jobTaskId,
        CancellationToken cancellationToken)
    {
        var config = await GetConfigAsync(cancellationToken);
        using var request = AjiApiRequestFactory.CreateGetResultOcrRequest(
            config.GetResultOcrUrl ?? string.Empty,
            config.InputOcrBearerToken ?? string.Empty,
            jobTaskId);
        return await SendAndLogAsync(
            request,
            "get_result_ocr",
            jobTaskId.Trim(),
            isApplicationSuccess: null,
            cancellationToken);
    }

    public async Task<AjiApiCallResult> SendFeedbackAsync(
        string jobTaskId,
        JsonElement ocrResult,
        CancellationToken cancellationToken)
    {
        var config = await GetConfigAsync(cancellationToken);
        using var request = AjiApiRequestFactory.CreateFeedbackRequest(
            config.FeedbackUrl ?? string.Empty,
            jobTaskId,
            ocrResult);
        return await SendAndLogAsync(
            request,
            "feedback",
            jobTaskId.Trim(),
            AjiApiRequestFactory.IsSuccessStatus,
            cancellationToken);
    }

    private async Task<InterfaceLimsOcrConfigApiEntity> GetConfigAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.InterfaceLimsOcrConfigApis
            .AsNoTracking()
            .OrderByDescending(x => x.CreateDate)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No config found in t_interface_lims_ocr_config_api.");
    }

    private async Task<AjiApiCallResult> SendAndLogAsync(
        HttpRequestMessage request,
        string apiName,
        string jobTaskId,
        Func<string?, bool>? isApplicationSuccess,
        CancellationToken cancellationToken)
    {
        var requestPayload = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var success = response.IsSuccessStatusCode
            && (isApplicationSuccess?.Invoke(responseBody) ?? true);

        dbContext.InterfaceLimsOcrLogs.Add(new InterfaceLimsOcrLogEntity
        {
            LogId = Guid.NewGuid(),
            ApiName = apiName,
            RequestUrl = request.RequestUri?.ToString(),
            JobTaskId = jobTaskId,
            RequestPayload = requestPayload,
            ResponseStatusCode = (int)response.StatusCode,
            ResponsePayload = responseBody,
            IsSuccess = success,
            ErrorMessage = success ? null : $"{apiName} returned an unsuccessful response",
            SourceSystem = "api_proxy",
            WorkStatus = success ? "completed_success" : "send_error",
            AttemptCount = 1,
            CompletedDate = DateTime.Now,
            IsInterface = success,
            CreateBy = "api",
            CreateDate = DateTime.Now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AjiApiCallResult(
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString() ?? "application/json",
            responseBody);
    }
}
