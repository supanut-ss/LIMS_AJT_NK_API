using System.Net;
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
                    await RecoverSendingJobsAsync(client, config, stoppingToken);
                    await FinalizeCompletedJobsAsync(config, stoppingToken);
                    await ProcessInboundFolderAsync(client, config, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while processing inbound files.");
            }

            try
            {
                await Task.Delay(await GetIntervalAsync(stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
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

    private async Task ProcessInboundFolderAsync(
        HttpClient client,
        LimsOcrConfigApiEntity config,
        CancellationToken stoppingToken)
    {
        if (!Directory.Exists(config.InboundDirectory))
        {
            return;
        }

        var inboundFiles = Directory
            .EnumerateFiles(config.InboundDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(file => string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(File.GetCreationTimeUtc)
            .ToList();

        foreach (var inboundFile in inboundFiles)
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (!SharedFilePolicy.IsReady(inboundFile, config.FileStableSeconds, DateTime.UtcNow))
            {
                continue;
            }

            try
            {
                await ProcessInboundFileAsync(client, config, inboundFile, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException ex)
            {
                logger.LogInformation(ex, "File was already claimed or became unavailable. File={File}", inboundFile);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process inbound file. File={File}", inboundFile);
            }
        }
    }

    private async Task ProcessInboundFileAsync(
        HttpClient client,
        LimsOcrConfigApiEntity config,
        string inboundFile,
        CancellationToken stoppingToken)
    {
        var originalFileName = Path.GetFileName(inboundFile);
        var processingPath = MoveToFolder(
            inboundFile,
            config.ProcessingDirectory,
            originalFileName);
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

        var trackedLog = new InterfaceLimsOcrLogEntity
        {
            LogId = Guid.NewGuid(),
            ApiName = "input_ocr",
            RequestUrl = config.InputOcrUrl,
            FlowId = payload.FlowId,
            JobTaskId = payload.JobTaskId,
            FilePath = processingPath,
            RequestPayload = JsonSerializer.Serialize(payload),
            IsSuccess = false,
            SourceSystem = "worker",
            WorkStatus = "sending",
            AttemptCount = 0,
            IsInterface = false,
            CreateBy = "worker",
            CreateDate = DateTime.Now
        };

        try
        {
            workerDbContext.InterfaceLimsOcrLogs.Add(trackedLog);
            await workerDbContext.SaveChangesAsync(stoppingToken);

            logger.LogInformation(
                "Claimed inbound file and created sending log. File={File}, FlowId={FlowId}, JobTaskId={JobTaskId}",
                processingPath,
                flowId,
                jobTaskId);

            await SendTrackedJobAsync(client, config, trackedLog, payload, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed after claiming input OCR file. File={File}", processingPath);

            if (workerDbContext.Entry(trackedLog).State != EntityState.Added)
            {
                trackedLog.WorkStatus = "send_error";
                trackedLog.ErrorMessage = ex.Message;
                trackedLog.CompletedDate = DateTime.Now;
                trackedLog.FinalPath = TryMoveToFolder(
                    processingPath,
                    config.ErrorDirectory,
                    originalFileName);
                trackedLog.FilePath = trackedLog.FinalPath;
                await workerDbContext.SaveChangesAsync(CancellationToken.None);
            }
            else
            {
                TryMoveToFolder(processingPath, config.ErrorDirectory, originalFileName);
            }
        }
    }

    private async Task RecoverSendingJobsAsync(
        HttpClient client,
        LimsOcrConfigApiEntity config,
        CancellationToken cancellationToken)
    {
        var sendingJobs = await workerDbContext.InterfaceLimsOcrLogs
            .Where(x => x.ApiName == "input_ocr" && x.WorkStatus == "sending")
            .OrderBy(x => x.CreateDate)
            .ToListAsync(cancellationToken);

        foreach (var job in sendingJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(job.RequestPayload)
                || string.IsNullOrWhiteSpace(job.FilePath)
                || !File.Exists(job.FilePath))
            {
                job.WorkStatus = "send_error";
                job.ErrorMessage = "Unable to recover sending job because payload or source file is missing";
                job.CompletedDate = DateTime.Now;
                await workerDbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            InputOcrRequest? payload;
            try
            {
                payload = JsonSerializer.Deserialize<InputOcrRequest>(job.RequestPayload);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Invalid request payload for sending job {LogId}.", job.LogId);
                payload = null;
            }

            if (payload is null)
            {
                job.WorkStatus = "send_error";
                job.ErrorMessage = "Unable to recover sending job because request payload is invalid";
                job.CompletedDate = DateTime.Now;
                await workerDbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            logger.LogWarning(
                "Recovering interrupted OCR send. LogId={LogId}, JobTaskId={JobTaskId}, AttemptCount={AttemptCount}",
                job.LogId,
                job.JobTaskId,
                job.AttemptCount);
            await SendTrackedJobAsync(client, config, job, payload, cancellationToken);
        }
    }

    private async Task SendTrackedJobAsync(
        HttpClient client,
        LimsOcrConfigApiEntity config,
        InterfaceLimsOcrLogEntity trackedLog,
        InputOcrRequest payload,
        CancellationToken cancellationToken)
    {
        var sendResult = await SendWithRetryAsync(client, config, payload, cancellationToken);
        trackedLog.AttemptCount += sendResult.AttemptCount;
        trackedLog.ResponseStatusCode = sendResult.StatusCode;
        trackedLog.ResponsePayload = sendResult.ResponseBody;

        if (sendResult.IsAccepted)
        {
            trackedLog.WorkStatus = "submitted";
            trackedLog.IsSuccess = true;
            trackedLog.ErrorMessage = null;

            logger.LogInformation(
                "input_ocr accepted. File stays in processing until callback arrives. JobTaskId={JobTaskId}, Attempts={Attempts}, Status={StatusCode}",
                payload.JobTaskId,
                sendResult.AttemptCount,
                sendResult.StatusCode);
        }
        else
        {
            var currentPath = trackedLog.FilePath!;
            var errorPath = TryMoveToFolder(
                currentPath,
                config.ErrorDirectory,
                GetOriginalFileName(currentPath));

            trackedLog.WorkStatus = "send_error";
            trackedLog.IsSuccess = false;
            trackedLog.ErrorMessage = sendResult.ErrorMessage;
            trackedLog.FilePath = errorPath;
            trackedLog.FinalPath = errorPath;
            trackedLog.CompletedDate = DateTime.Now;

            logger.LogError(
                "input_ocr failed after retries. JobTaskId={JobTaskId}, Attempts={Attempts}, Status={StatusCode}, File={File}",
                payload.JobTaskId,
                sendResult.AttemptCount,
                sendResult.StatusCode,
                errorPath);
        }

        await workerDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<OcrSendResult> SendWithRetryAsync(
        HttpClient client,
        LimsOcrConfigApiEntity config,
        InputOcrRequest payload,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(config.MaxSendAttempts <= 0 ? 3 : config.MaxSendAttempts, 1, 10);
        var retryDelaySeconds = Math.Clamp(config.RetryDelaySeconds <= 0 ? 5 : config.RetryDelaySeconds, 1, 60);
        var timeoutSeconds = Math.Clamp(config.RequestTimeoutSeconds <= 0 ? 60 : config.RequestTimeoutSeconds, 1, 600);
        int? lastStatusCode = null;
        string? lastResponseBody = null;
        string? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                using var response = await client.PostAsJsonAsync(
                    config.InputOcrUrl,
                    payload,
                    timeoutSource.Token);
                lastStatusCode = (int)response.StatusCode;
                lastResponseBody = await response.Content.ReadAsStringAsync(timeoutSource.Token);

                if (response.IsSuccessStatusCode)
                {
                    return new OcrSendResult(
                        true,
                        attempt,
                        lastStatusCode,
                        lastResponseBody,
                        null);
                }

                lastError = $"input_ocr returned HTTP {lastStatusCode}";
                if (!SharedFilePolicy.IsTransientStatus(response.StatusCode) || attempt == maxAttempts)
                {
                    return new OcrSendResult(
                        false,
                        attempt,
                        lastStatusCode,
                        lastResponseBody,
                        lastError);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                lastError = $"input_ocr timed out after {timeoutSeconds} seconds";
            }
            catch (HttpRequestException ex)
            {
                lastError = ex.Message;
            }

            logger.LogWarning(
                "Transient input_ocr failure. JobTaskId={JobTaskId}, Attempt={Attempt}/{MaxAttempts}, Error={Error}",
                payload.JobTaskId,
                attempt,
                maxAttempts,
                lastError);

            if (attempt < maxAttempts)
            {
                var delaySeconds = Math.Min(
                    retryDelaySeconds * (int)Math.Pow(2, attempt - 1),
                    60);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        return new OcrSendResult(
            false,
            maxAttempts,
            lastStatusCode,
            lastResponseBody,
            lastError ?? "input_ocr failed");
    }

    private async Task FinalizeCompletedJobsAsync(
        LimsOcrConfigApiEntity config,
        CancellationToken cancellationToken)
    {
        var pendingJobs = await workerDbContext.InterfaceLimsOcrLogs
            .AsNoTracking()
            .Where(x => x.ApiName == "input_ocr"
                && x.WorkStatus == "submitted"
                && x.JobTaskId != null
                && x.FilePath != null)
            .OrderBy(x => x.CreateDate)
            .ToListAsync(cancellationToken);

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

            var callbackSucceeded = IsCallbackSuccess(
                callbackLog.RequestPayload,
                callbackLog.ResponsePayload);
            var destinationFolder = callbackSucceeded
                ? config.SuccessDirectory
                : config.ErrorDirectory;
            var destinationPath = TryMoveToFolder(
                job.FilePath!,
                destinationFolder,
                GetOriginalFileName(job.FilePath!));
            var trackedLog = await workerDbContext.InterfaceLimsOcrLogs
                .FirstOrDefaultAsync(x => x.LogId == job.LogId, cancellationToken);

            if (trackedLog is null)
            {
                continue;
            }

            trackedLog.WorkStatus = callbackSucceeded
                ? "completed_success"
                : "completed_error";
            trackedLog.FinalPath = destinationPath;
            trackedLog.CompletedDate = DateTime.Now;
            trackedLog.IsSuccess = callbackSucceeded;
            trackedLog.IsInterface = callbackSucceeded;
            trackedLog.ErrorMessage = callbackSucceeded
                ? null
                : "callback returned fail or interface was not completed";
            trackedLog.ResponseStatusCode = callbackLog.ResponseStatusCode;
            trackedLog.ResponsePayload = callbackLog.ResponsePayload;

            await workerDbContext.SaveChangesAsync(cancellationToken);
        }
    }

    internal static bool IsCallbackSuccess(string? requestPayload, string? responsePayload)
    {
        if (!HasReadyToCheckResult(requestPayload))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(responsePayload))
        {
            return true;
        }

        try
        {
            using var response = JsonDocument.Parse(responsePayload);
            if (response.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("interface_status", out var interfaceStatus))
            {
                return string.Equals(
                    interfaceStatus.GetString(),
                    "completed",
                    StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasReadyToCheckResult(string? requestPayload)
    {
        if (string.IsNullOrWhiteSpace(requestPayload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(requestPayload);
            if (!document.RootElement.TryGetProperty("ocr_result", out var ocrResult)
                || ocrResult.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var readyToCheck = false;
            foreach (var page in ocrResult.EnumerateArray())
            {
                if (!page.TryGetProperty("tracking_status", out var statusElement))
                {
                    continue;
                }

                var status = statusElement.GetString();
                if (string.Equals(status, "fail", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                readyToCheck |= string.Equals(
                    status,
                    "ReadyToCheck",
                    StringComparison.OrdinalIgnoreCase);
            }

            return readyToCheck;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void EnsureDirectories(LimsOcrConfigApiEntity config)
    {
        Directory.CreateDirectory(config.InboundDirectory);
        Directory.CreateDirectory(config.ProcessingDirectory);
        Directory.CreateDirectory(config.SuccessDirectory);
        Directory.CreateDirectory(config.ErrorDirectory);
    }

    private static string MoveToFolder(
        string sourcePath,
        string destinationFolder,
        string originalFileName)
    {
        Directory.CreateDirectory(destinationFolder);
        var destinationPath = Path.Combine(
            destinationFolder,
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}_{originalFileName}");

        File.Move(sourcePath, destinationPath, overwrite: false);
        return destinationPath;
    }

    private string TryMoveToFolder(
        string sourcePath,
        string destinationFolder,
        string originalFileName)
    {
        if (!File.Exists(sourcePath))
        {
            return sourcePath;
        }

        try
        {
            return MoveToFolder(sourcePath, destinationFolder, originalFileName);
        }
        catch (IOException ex)
        {
            logger.LogError(
                ex,
                "Unable to move file. Source={Source}, Destination={Destination}",
                sourcePath,
                destinationFolder);
            return sourcePath;
        }
    }

    private static string GetOriginalFileName(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        const int workerPrefixLength = 51;

        return fileName.Length > workerPrefixLength
            && fileName[17] == '_'
            && fileName[50] == '_'
                ? fileName[workerPrefixLength..]
                : fileName;
    }

    private sealed record OcrSendResult(
        bool IsAccepted,
        int AttemptCount,
        int? StatusCode,
        string? ResponseBody,
        string? ErrorMessage);
}

public static class SharedFilePolicy
{
    public static bool IsReady(string filePath, int stableSeconds, DateTime utcNow)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length <= 0)
            {
                return false;
            }

            var requiredAge = TimeSpan.FromSeconds(Math.Clamp(stableSeconds, 0, 300));
            if (utcNow - fileInfo.LastWriteTimeUtc < requiredAge)
            {
                return false;
            }

            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
            return stream.Length == fileInfo.Length;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        var value = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || value >= 500;
    }
}
