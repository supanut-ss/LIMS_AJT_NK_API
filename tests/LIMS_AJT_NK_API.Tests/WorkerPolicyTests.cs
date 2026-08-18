using System.Net;
using LIMS_AJT_NK_CallbackWorker;

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

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"LimsWorkerPolicyTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
