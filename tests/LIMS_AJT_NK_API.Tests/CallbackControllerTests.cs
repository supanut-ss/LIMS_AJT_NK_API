using System.Text.Json;
using LIMS_AJT_NK_API.Controllers;
using LIMS_AJT_NK_API.Models;
using LIMS_AJT_NK_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LIMS_AJT_NK_API.Tests;

public class CallbackControllerTests
{
    [Fact]
    public async Task ReceiveOcrResult_ReturnsInterfaceSummaryAndWritesProcessingLog()
    {
        await using var database = await TestDatabase.CreateAsync();
        var summary = new OcrCallbackInterfaceSummary
        {
            InterfaceStatus = OcrInterfaceStatuses.Partial,
            UpdatedItemCount = 1,
            SkippedItemCount = 1,
            Pages =
            [
                new OcrCallbackInterfacePageResult
                {
                    PageId = 1,
                    Status = "partial",
                    UpdatedItemCount = 1,
                    SkippedItemCount = 1
                }
            ]
        };
        var controller = CreateController(database.Context, new StubInterfaceService(summary));

        var actionResult = await controller.ReceiveOcrResult(CreateRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        using var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal("partial", responseJson.RootElement.GetProperty("data").GetProperty("interface_status").GetString());
        Assert.Equal(1, responseJson.RootElement.GetProperty("data").GetProperty("updated_item_count").GetInt32());
        Assert.Single(await database.Context.InterfaceLimsOcrCallbacks.ToListAsync());
        Assert.Equal("callback_partial", (await database.Context.InterfaceLimsOcrLogs.SingleAsync()).WorkStatus);
    }

    [Fact]
    public async Task ReceiveOcrResult_Returns500AndKeepsRawCallback_WhenInterfaceServiceFails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = CreateController(
            database.Context,
            new StubInterfaceService(new InvalidOperationException("Simulated interface failure")));

        var actionResult = await controller.ReceiveOcrResult(CreateRequest(), CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(500, error.StatusCode);
        Assert.Single(await database.Context.InterfaceLimsOcrCallbacks.ToListAsync());
        var log = await database.Context.InterfaceLimsOcrLogs.SingleAsync();
        Assert.Equal("callback_error", log.WorkStatus);
        Assert.False(log.IsInterface);
    }

    private static CallbackController CreateController(
        Data.ApplicationDbContext context,
        IOcrCallbackInterfaceService service)
    {
        return new CallbackController(context, service, NullLogger<CallbackController>.Instance);
    }

    private static OcrCallbackRequest CreateRequest()
    {
        return new OcrCallbackRequest
        {
            JobTaskId = "test-job",
            OcrResult =
            [
                new OcrResultRequest
                {
                    PageId = 1,
                    TrackingStatus = "ReadyToCheck",
                    BodyJson = new OcrBodyJsonRequest
                    {
                        ProductName = "Product",
                        SupplierName = "Supplier",
                        LotNumber = "LOT-1",
                        MfgDate = "01/01/2026",
                        BodyItem =
                        [
                            new OcrBodyItemRequest
                            {
                                ParameterName = "Moisture",
                                Result = "5"
                            }
                        ]
                    }
                }
            ]
        };
    }

    private sealed class StubInterfaceService : IOcrCallbackInterfaceService
    {
        private readonly OcrCallbackInterfaceSummary? summary;
        private readonly Exception? exception;

        public StubInterfaceService(OcrCallbackInterfaceSummary summary)
        {
            this.summary = summary;
        }

        public StubInterfaceService(Exception exception)
        {
            this.exception = exception;
        }

        public Task<OcrCallbackInterfaceSummary> ProcessAsync(
            Guid callbackId,
            CancellationToken cancellationToken = default)
        {
            return exception is not null
                ? Task.FromException<OcrCallbackInterfaceSummary>(exception)
                : Task.FromResult(summary!);
        }
    }

}
