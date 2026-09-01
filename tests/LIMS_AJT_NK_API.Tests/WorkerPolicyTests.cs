using System.Net;
using System.Text.Json;
using LIMS_AJT_NK_CallbackWorker;
using LIMS_AJT_NK_CallbackWorker.Models;

namespace LIMS_AJT_NK_API.Tests;

public class WorkerPolicyTests
{
    [Fact]
    public async Task IsReady_AcceptsStableNonEmptyPdf()
    {
        var testDirectory = CreateTestDirectory();
        var filePath = Path.Combine(testDirectory, "coa.pdf");

        try
        {
            await File.WriteAllBytesAsync(filePath, "%PDF-1.4 test"u8.ToArray());
            File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(-1));

            Assert.True(SharedFilePolicy.IsReady(filePath, 5, DateTime.UtcNow));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task IsReady_RejectsFileThatWasJustWrittenOrIsEmpty()
    {
        var testDirectory = CreateTestDirectory();
        var freshFile = Path.Combine(testDirectory, "fresh.pdf");
        var emptyFile = Path.Combine(testDirectory, "empty.pdf");

        try
        {
            await File.WriteAllBytesAsync(freshFile, "%PDF-1.4 test"u8.ToArray());
            await File.WriteAllBytesAsync(emptyFile, []);
            File.SetLastWriteTimeUtc(emptyFile, DateTime.UtcNow.AddMinutes(-1));

            Assert.False(SharedFilePolicy.IsReady(freshFile, 30, DateTime.UtcNow));
            Assert.False(SharedFilePolicy.IsReady(emptyFile, 0, DateTime.UtcNow));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public void IsTransientStatus_ClassifiesRetryableResponses(
        HttpStatusCode statusCode,
        bool expected)
    {
        Assert.Equal(expected, SharedFilePolicy.IsTransientStatus(statusCode));
    }

    [Theory]
    [InlineData(null, OcrSubmissionPolicy.InputOcrApiName)]
    [InlineData("", OcrSubmissionPolicy.InputOcrApiName)]
    [InlineData("path", OcrSubmissionPolicy.InputOcrApiName)]
    [InlineData("FILE", OcrSubmissionPolicy.InputOcrFileApiName)]
    public void ResolveApiName_SelectsConfiguredRoute(string? mode, string expected)
    {
        Assert.Equal(expected, OcrSubmissionPolicy.ResolveApiName(mode));
    }

    [Fact]
    public void ResolveApiName_RejectsUnknownRoute()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => OcrSubmissionPolicy.ResolveApiName("unexpected"));

        Assert.Contains("submission_mode", error.Message);
    }

    [Fact]
    public void ResolveRequestUrl_UsesPersistedFileRouteUrlAfterConfigSwitch()
    {
        var config = new LimsOcrConfigApiEntity
        {
            InputOcrUrl = "https://new.test/input_ocr",
            InputOcrFileUrl = "https://new.test/input_ocr_file",
            SubmissionMode = OcrSubmissionPolicy.PathSubmissionMode
        };

        var url = OcrSubmissionPolicy.ResolveRequestUrl(
            config,
            OcrSubmissionPolicy.InputOcrFileApiName,
            " https://original.test/input_ocr_file ");

        Assert.Equal("https://original.test/input_ocr_file", url);
    }

    [Fact]
    public async Task CreateContent_PathRouteCreatesExistingJsonContract()
    {
        var payload = CreatePayload("source.pdf");

        using var content = OcrSubmissionPolicy.CreateContent(
            OcrSubmissionPolicy.InputOcrApiName,
            payload,
            payload.S3PathImage,
            "ignored");
        using var json = JsonDocument.Parse(await content.ReadAsStringAsync());

        Assert.Equal("application/json", content.Headers.ContentType?.MediaType);
        Assert.Equal("flow-1", json.RootElement.GetProperty("flow_id").GetString());
        Assert.Equal("job-1", json.RootElement.GetProperty("job_task_id").GetString());
        Assert.Equal("source.pdf", json.RootElement.GetProperty("s3_path_image").GetString());
        Assert.Equal("https://lims.test/api/call_back", json.RootElement.GetProperty("callback_url").GetString());
    }

    [Fact]
    public async Task CreateContent_FileRouteCreatesMultipartPdfContract()
    {
        var testDirectory = CreateTestDirectory();
        var originalName = "coa result.pdf";
        var prefixedName = $"20260901123456789_{Guid.NewGuid():N}_{originalName}";
        var filePath = Path.Combine(testDirectory, prefixedName);
        var expectedBytes = "%PDF-1.7 multipart test"u8.ToArray();

        try
        {
            await File.WriteAllBytesAsync(filePath, expectedBytes);
            var payload = CreatePayload(filePath);

            using var content = Assert.IsType<MultipartFormDataContent>(
                OcrSubmissionPolicy.CreateContent(
                    OcrSubmissionPolicy.InputOcrFileApiName,
                    payload,
                    filePath,
                    "upload_pdf"));
            var parts = content.ToList();

            Assert.Equal("multipart/form-data", content.Headers.ContentType?.MediaType);
            Assert.Equal("flow-1", await ReadTextPartAsync(parts, "flow_id"));
            Assert.Equal("job-1", await ReadTextPartAsync(parts, "job_task_id"));
            Assert.Equal(
                "https://lims.test/api/call_back",
                await ReadTextPartAsync(parts, "callback_url"));

            var filePart = Assert.Single(parts, x => PartName(x) == "upload_pdf");
            Assert.Equal("application/pdf", filePart.Headers.ContentType?.MediaType);
            Assert.Equal(originalName, filePart.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.Equal(expectedBytes, await filePart.ReadAsByteArrayAsync());
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateContent_FileRouteOmitsBlankCallbackAndDefaultsFileField()
    {
        var testDirectory = CreateTestDirectory();
        var filePath = Path.Combine(testDirectory, "coa.pdf");

        try
        {
            await File.WriteAllBytesAsync(filePath, "%PDF-1.4"u8.ToArray());
            var payload = CreatePayload(filePath);
            payload.CallbackUrl = " ";

            using var content = Assert.IsType<MultipartFormDataContent>(
                OcrSubmissionPolicy.CreateContent(
                    OcrSubmissionPolicy.InputOcrFileApiName,
                    payload,
                    filePath,
                    null));
            var parts = content.ToList();

            Assert.DoesNotContain(parts, x => PartName(x) == "callback_url");
            Assert.Single(parts, x => PartName(x) == "file");
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateContent_FileRouteReopensPdfForRetry()
    {
        var testDirectory = CreateTestDirectory();
        var filePath = Path.Combine(testDirectory, "retry.pdf");
        var expectedBytes = "%PDF-1.7 retry"u8.ToArray();

        try
        {
            await File.WriteAllBytesAsync(filePath, expectedBytes);
            var payload = CreatePayload(filePath);

            using (var firstAttempt = OcrSubmissionPolicy.CreateContent(
                       OcrSubmissionPolicy.InputOcrFileApiName,
                       payload,
                       filePath,
                       "file"))
            {
                var firstFile = Assert.Single(
                    Assert.IsType<MultipartFormDataContent>(firstAttempt),
                    x => PartName(x) == "file");
                Assert.Equal(expectedBytes, await firstFile.ReadAsByteArrayAsync());
            }

            using var retryAttempt = OcrSubmissionPolicy.CreateContent(
                OcrSubmissionPolicy.InputOcrFileApiName,
                payload,
                filePath,
                "file");
            var retryFile = Assert.Single(
                Assert.IsType<MultipartFormDataContent>(retryAttempt),
                x => PartName(x) == "file");
            Assert.Equal(expectedBytes, await retryFile.ReadAsByteArrayAsync());
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static InputOcrRequest CreatePayload(string filePath)
    {
        return new InputOcrRequest
        {
            FlowId = "flow-1",
            JobTaskId = "job-1",
            S3PathImage = filePath,
            CallbackUrl = "https://lims.test/api/call_back"
        };
    }

    private static async Task<string> ReadTextPartAsync(
        IEnumerable<HttpContent> parts,
        string name)
    {
        return await Assert.Single(parts, x => PartName(x) == name).ReadAsStringAsync();
    }

    private static string? PartName(HttpContent content)
    {
        return content.Headers.ContentDisposition?.Name?.Trim('"');
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"LimsWorkerPolicyTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
