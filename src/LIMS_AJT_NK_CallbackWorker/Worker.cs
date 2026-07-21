using System.Net.Http.Json;
using System.Text.Json;
using LIMS_AJT_NK_CallbackWorker.Data;
using LIMS_AJT_NK_CallbackWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_CallbackWorker;

public class Worker(
    ILogger<Worker> logger,
    IHttpClientFactory httpClientFactory,
    WorkerDbContext workerDbContext) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = httpClientFactory.CreateClient();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = await workerDbContext.LimsOcrConfigApis
                    .AsNoTracking()
                    .OrderByDescending(x => x.CreateDate)
                    .FirstOrDefaultAsync(stoppingToken);

                if (config is null)
                {
                    logger.LogWarning("No config found in t_interface_lims_ocr_config_api.");
                }
                else if (!config.IsEnabled)
                {
                    logger.LogInformation("Input OCR worker disabled by database config.");
                }
                else
                {
                    EnsureDirectories(config);
                    await FinalizeCompletedJobsAsync(config, stoppingToken);
                    await ProcessInboundFolderAsync(client, config, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while processing inbound files.");
            }

            var interval = await GetIntervalAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task<TimeSpan> GetIntervalAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = await workerDbContext.LimsOcrConfigApis
            .AsNoTracking()
            .OrderByDescending(x => x.CreateDate)
            .Select(x => x.IntervalSeconds)
            .FirstOrDefaultAsync(stoppingToken);

        return TimeSpan.FromSeconds(intervalSeconds <= 0 ? 30 : intervalSeconds);
    }

    private async Task ProcessInboundFolderAsync(HttpClient client, LimsOcrConfigApiEntity config, CancellationToken stoppingToken)
    {
        if (!Directory.Exists(config.InboundDirectory))
        {
            return;
        }

        var inboundFiles = Directory
            .EnumerateFiles(config.InboundDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(file => string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => File.GetCreationTimeUtc(file))
            .ToList();

        foreach (var inboundFile in inboundFiles)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (!IsFileReady(inboundFile))
            {
                continue;
            }

            var fileName = Path.GetFileName(inboundFile);
            var processingPath = MoveToFolder(inboundFile, config.ProcessingDirectory, fileName);

            var flowId = string.IsNullOrWhiteSpace(config.FlowId)
                ? Guid.NewGuid().ToString()
                : config.FlowId;
            var jobTaskId = Guid.NewGuid().ToString();

            var payload = new InputOcrRequest
            {
                FlowId = flowId,
                JobTaskId = jobTaskId,
                S3PathImage = processingPath,
                CallbackUrl = config.CallbackUrl ?? string.Empty
            };
            var requestPayload = JsonSerializer.Serialize(payload);

            logger.LogInformation("Sending input_ocr. File={File}, FlowId={FlowId}, JobTaskId={JobTaskId}", processingPath, flowId, jobTaskId);

            try
            {
                using var response = await client.PostAsJsonAsync(config.InputOcrUrl, payload, stoppingToken);
                var responseBody = await response.Content.ReadAsStringAsync(stoppingToken);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("input_ocr accepted. File stays in processing until callback arrives. Status={StatusCode}, Body={Body}", (int)response.StatusCode, responseBody);

                    await WriteInterfaceLogAsync(
                        apiName: "input_ocr",
                        requestUrl: config.InputOcrUrl,
                        flowId: payload.FlowId,
                        jobTaskId: payload.JobTaskId,
                        filePath: processingPath,
                        requestPayload: requestPayload,
                        responseStatusCode: (int)response.StatusCode,
                        responsePayload: responseBody,
                        isSuccess: true,
                        errorMessage: null,
                        workStatus: "submitted",
                        finalPath: null,
                        completedDate: null,
                        cancellationToken: stoppingToken);
                }
                else
                {
                    var errorPath = MoveToFolder(processingPath, config.ErrorDirectory, fileName);
                    logger.LogWarning("input_ocr failed. File moved to {File}. Status={StatusCode}, Body={Body}", errorPath, (int)response.StatusCode, responseBody);

                    await WriteInterfaceLogAsync(
                        apiName: "input_ocr",
                        requestUrl: config.InputOcrUrl,
                        flowId: payload.FlowId,
                        jobTaskId: payload.JobTaskId,
                        filePath: errorPath,
                        requestPayload: requestPayload,
                        responseStatusCode: (int)response.StatusCode,
                        responsePayload: responseBody,
                        isSuccess: false,
                        errorMessage: "input_ocr rejected request",
                        workStatus: "send_error",
                        finalPath: errorPath,
                        completedDate: DateTime.Now,
                        cancellationToken: stoppingToken);
                }
            }
            catch (Exception ex)
            {
                var errorPath = MoveToFolder(processingPath, config.ErrorDirectory, fileName);
                logger.LogError(ex, "Error while sending input_ocr. File moved to {File}.", errorPath);

                await WriteInterfaceLogAsync(
                    apiName: "input_ocr",
                    requestUrl: config.InputOcrUrl,
                    flowId: payload.FlowId,
                    jobTaskId: payload.JobTaskId,
                    filePath: errorPath,
                    requestPayload: requestPayload,
                    responseStatusCode: null,
                    responsePayload: null,
                    isSuccess: false,
                    errorMessage: ex.Message,
                    workStatus: "send_error",
                    finalPath: errorPath,
                    completedDate: DateTime.Now,
                    cancellationToken: stoppingToken);
            }
        }
    }

    private async Task FinalizeCompletedJobsAsync(LimsOcrConfigApiEntity config, CancellationToken cancellationToken)
    {
        var pendingJobs = await workerDbContext.InterfaceLimsOcrLogs
            .AsNoTracking()
            .Where(x => x.ApiName == "input_ocr" && x.WorkStatus == "submitted" && x.JobTaskId != null && x.FilePath != null)
            .OrderBy(x => x.CreateDate)
            .ToListAsync(cancellationToken);

        if (pendingJobs.Count == 0)
        {
            return;
        }

        foreach (var job in pendingJobs)
        {
            var callbackLog = await workerDbContext.InterfaceLimsOcrLogs
                .AsNoTracking()
                .Where(x => x.ApiName == "call_back" && x.JobTaskId == job.JobTaskId)
                .OrderByDescending(x => x.CreateDate)
                .FirstOrDefaultAsync(cancellationToken);

            if (callbackLog is null)
            {
                continue;
            }

            var callbackSucceeded = IsCallbackSuccess(callbackLog.RequestPayload);
            var destinationFolder = callbackSucceeded ? config.SuccessDirectory : config.ErrorDirectory;
            var destinationPath = MoveToFolder(job.FilePath!, destinationFolder, Path.GetFileName(job.FilePath!));

            var trackedLog = await workerDbContext.InterfaceLimsOcrLogs
                .FirstOrDefaultAsync(x => x.LogId == job.LogId, cancellationToken);

            if (trackedLog is null)
            {
                continue;
            }

            trackedLog.WorkStatus = callbackSucceeded ? "completed_success" : "completed_error";
            trackedLog.FinalPath = destinationPath;
            trackedLog.CompletedDate = DateTime.Now;
            trackedLog.IsSuccess = callbackSucceeded;
            trackedLog.ErrorMessage = callbackSucceeded ? null : "callback returned fail status";
            trackedLog.ResponseStatusCode = callbackSucceeded ? 200 : 500;
            trackedLog.ResponsePayload = callbackLog.ResponsePayload;

            await workerDbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsCallbackSuccess(string? requestPayload)
    {
        if (string.IsNullOrWhiteSpace(requestPayload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(requestPayload);
            if (!document.RootElement.TryGetProperty("ocr_result", out var ocrResult) || ocrResult.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var page in ocrResult.EnumerateArray())
            {
                if (!page.TryGetProperty("tracking_status", out var trackingStatusElement))
                {
                    continue;
                }

                var trackingStatus = trackingStatusElement.GetString();
                if (string.Equals(trackingStatus, "fail", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(trackingStatus, "ReadyToCheck", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task WriteInterfaceLogAsync(
        string apiName,
        string? requestUrl,
        string? flowId,
        string? jobTaskId,
        string? filePath,
        string? requestPayload,
        int? responseStatusCode,
        string? responsePayload,
        bool isSuccess,
        string? errorMessage,
        string workStatus,
        string? finalPath,
        DateTime? completedDate,
        CancellationToken cancellationToken)
    {
        try
        {
            workerDbContext.InterfaceLimsOcrLogs.Add(new InterfaceLimsOcrLogEntity
            {
                LogId = Guid.NewGuid(),
                ApiName = apiName,
                RequestUrl = requestUrl,
                FlowId = flowId,
                JobTaskId = jobTaskId,
                FilePath = filePath,
                RequestPayload = requestPayload,
                ResponseStatusCode = responseStatusCode,
                ResponsePayload = responsePayload,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage,
                SourceSystem = "worker",
                WorkStatus = workStatus,
                FinalPath = finalPath,
                CompletedDate = completedDate,
                IsInterface = false,
                CreateBy = "worker",
                CreateDate = DateTime.Now
            });

            await workerDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write interface log.");
        }
    }

    private static void EnsureDirectories(LimsOcrConfigApiEntity config)
    {
        Directory.CreateDirectory(config.InboundDirectory);
        Directory.CreateDirectory(config.ProcessingDirectory);
        Directory.CreateDirectory(config.SuccessDirectory);
        Directory.CreateDirectory(config.ErrorDirectory);
    }

    private static bool IsFileReady(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length >= 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string MoveToFolder(string sourcePath, string destinationFolder, string originalFileName)
    {
        Directory.CreateDirectory(destinationFolder);

        var destinationPath = Path.Combine(
            destinationFolder,
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}_{originalFileName}");

        File.Move(sourcePath, destinationPath, true);
        return destinationPath;
    }
}
