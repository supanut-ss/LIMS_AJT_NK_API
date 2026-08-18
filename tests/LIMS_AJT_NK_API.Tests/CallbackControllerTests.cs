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
    public async Task ReceiveOcrResult_AcceptsActualFileIdPayloadAndPersistsNormalizedValues()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = CreateController(
            database.Context,
            new StubInterfaceService(new OcrCallbackInterfaceSummary
            {
                InterfaceStatus = OcrInterfaceStatuses.NotMatched,
                SkippedItemCount = 9
            }));
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "refined_salt_callback.json");
        var request = JsonSerializer.Deserialize<OcrCallbackRequest>(await File.ReadAllTextAsync(fixturePath));

        var actionResult = await controller.ReceiveOcrResult(request!, CancellationToken.None);

        Assert.IsType<OkObjectResult>(actionResult);
        database.Context.ChangeTracker.Clear();
        var callback = await database.Context.InterfaceLimsOcrCallbacks
            .Include(x => x.Results)
            .ThenInclude(x => x.Items)
            .SingleAsync();
        var result = Assert.Single(callback.Results);
        Assert.Equal(1, result.PageId);
        Assert.Equal("6a8283ddb3b7d074da97e8f1", result.FileId);
        Assert.Equal("26KC052501", result.LotNumber);
        Assert.Equal("26KC052501", result.InternalLot);
        Assert.Equal(94000m, result.Quantity);
        Assert.Equal("kg", result.QuantityUom);
        Assert.Equal(new DateTime(2026, 5, 8), result.MfgDate);
        Assert.Equal(new DateTime(2029, 5, 8), result.ExpiryDate);
        Assert.Equal(9, result.Items.Count);
        Assert.Contains("\"product_name\":0.8824", result.ConfidenceJson);
    }

    [Fact]
    public async Task ReceiveOcrResult_ProcessesActualPayloadEndToEnd_WhenLimsMasterDataMatches()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "refined_salt_callback.json");
        var request = JsonSerializer.Deserialize<OcrCallbackRequest>(await File.ReadAllTextAsync(fixturePath))!;
        await SeedRefinedSaltScenarioAsync(database.Context, request);
        var controller = CreateController(
            database.Context,
            new OcrCallbackInterfaceService(
                database.Context,
                NullLogger<OcrCallbackInterfaceService>.Instance));

        var actionResult = await controller.ReceiveOcrResult(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        using var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var responseData = responseJson.RootElement.GetProperty("data");
        Assert.Equal(OcrInterfaceStatuses.Completed, responseData.GetProperty("interface_status").GetString());
        Assert.Equal(9, responseData.GetProperty("updated_item_count").GetInt32());
        Assert.Equal(0, responseData.GetProperty("skipped_item_count").GetInt32());

        database.Context.ChangeTracker.Clear();
        Assert.Equal(9, await database.Context.LimsQaqcCoaParameterTransactions.CountAsync());
        Assert.All(
            await database.Context.LimsQaqcCoaParameterTests.ToListAsync(),
            test =>
            {
                Assert.Equal("api", test.UpdateBy);
                Assert.NotEqual("old", test.Result);
            });
        var callback = await database.Context.InterfaceLimsOcrCallbacks
            .Include(x => x.Results)
            .ThenInclude(x => x.Items)
            .SingleAsync();
        Assert.True(callback.IsInterface);
        Assert.True(callback.Results.Single().IsInterface);
        Assert.All(callback.Results.Single().Items, item => Assert.True(item.IsInterface));
        Assert.Equal("callback_processed", (await database.Context.InterfaceLimsOcrLogs.SingleAsync()).WorkStatus);
    }

    [Fact]
    public async Task ReceiveOcrResult_CopiesSourceFileToHostAndRegistersLimsDocumentOnce()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"LimsOcrDocumentTests_{Guid.NewGuid():N}");
        var processingDirectory = Path.Combine(testRoot, "processing");
        var hostDirectory = Path.Combine(testRoot, "host", "_Documents");
        Directory.CreateDirectory(processingDirectory);
        var originalFileName = "1. REFINED SALT 26KC052501.pdf";
        var sourcePath = Path.Combine(
            processingDirectory,
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}_{originalFileName}");
        await File.WriteAllBytesAsync(sourcePath, "%PDF-1.4 test"u8.ToArray());

        try
        {
            await using var database = await TestDatabase.CreateAsync();
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "refined_salt_callback.json");
            var request = JsonSerializer.Deserialize<OcrCallbackRequest>(await File.ReadAllTextAsync(fixturePath))!;
            var receiptDetailId = await SeedRefinedSaltScenarioAsync(database.Context, request);
            database.Context.InterfaceLimsOcrConfigApis.Add(new InterfaceLimsOcrConfigApiEntity
            {
                ConfigId = Guid.NewGuid(),
                IsEnabled = true,
                InputOcrUrl = "http://ocr/input_ocr",
                CallbackUrl = "http://lims/api/call_back",
                InboundDirectory = Path.Combine(testRoot, "inbound"),
                ProcessingDirectory = processingDirectory,
                SuccessDirectory = Path.Combine(testRoot, "success"),
                ErrorDirectory = Path.Combine(testRoot, "error"),
                DocumentHostDirectory = hostDirectory,
                DocumentWebPath = "../_Documents",
                DocumentGroup = "QC_COA",
                IntervalSeconds = 30,
                CreateBy = "test",
                CreateDate = DateTime.Now
            });
            database.Context.InterfaceLimsOcrLogs.Add(new InterfaceLimsOcrLogEntity
            {
                LogId = Guid.NewGuid(),
                ApiName = "input_ocr",
                JobTaskId = request.JobTaskId,
                FilePath = sourcePath,
                WorkStatus = "submitted",
                SourceSystem = "worker",
                CreateBy = "test",
                CreateDate = DateTime.Now
            });
            await database.Context.SaveChangesAsync();

            var controller = CreateController(
                database.Context,
                new OcrCallbackInterfaceService(
                    database.Context,
                    NullLogger<OcrCallbackInterfaceService>.Instance));

            var firstActionResult = await controller.ReceiveOcrResult(request, CancellationToken.None);
            var firstOk = Assert.IsType<OkObjectResult>(firstActionResult);

            database.Context.ChangeTracker.Clear();
            var document = await database.Context.LimsDocuments.SingleAsync();
            Assert.Equal(receiptDetailId, document.PkId);
            Assert.Equal("QC_COA", document.DocumentGroup);
            Assert.Equal("application/pdf", document.DocumentType);
            Assert.Equal("--", document.DocumentCode);
            Assert.Equal(originalFileName, document.DocumentName);
            Assert.Equal(request.JobTaskId, document.Udf1);
            Assert.Equal("6a8283ddb3b7d074da97e8f1", document.Udf3);
            Assert.StartsWith("../_Documents/QC_COA/", document.DocumentPath);
            Assert.True(File.Exists(Path.Combine(hostDirectory, "QC_COA", Path.GetFileName(document.DocumentPath!))));

            using (var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(firstOk.Value)))
            {
                var page = responseJson.RootElement.GetProperty("data").GetProperty("pages")[0];
                Assert.Equal("stored", page.GetProperty("document_status").GetString());
                Assert.Equal(document.DocumentId, page.GetProperty("document_id").GetInt32());
                Assert.Equal(document.DocumentPath, page.GetProperty("document_path").GetString());
            }

            var secondActionResult = await controller.ReceiveOcrResult(request, CancellationToken.None);
            var secondOk = Assert.IsType<OkObjectResult>(secondActionResult);
            database.Context.ChangeTracker.Clear();
            Assert.Equal(1, await database.Context.LimsDocuments.CountAsync());

            using var secondResponseJson = JsonDocument.Parse(JsonSerializer.Serialize(secondOk.Value));
            var secondPage = secondResponseJson.RootElement.GetProperty("data").GetProperty("pages")[0];
            Assert.Equal("already_exists", secondPage.GetProperty("document_status").GetString());
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

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

    private static async Task<Guid> SeedRefinedSaltScenarioAsync(
        Data.ApplicationDbContext context,
        OcrCallbackRequest request)
    {
        var receiptDetailId = Guid.NewGuid();
        var itemMasterId = Guid.NewGuid();
        var itemRmId = Guid.NewGuid();
        context.WmsItems.Add(new WmsItemEntity
        {
            ItemMasterId = itemMasterId,
            Description = "REFINED SALT 50 KG."
        });
        context.LimsInboundReceives.Add(new LimsInboundReceiveEntity
        {
            ReceiptDetailId = receiptDetailId,
            ItemMasterId = itemMasterId,
            SupplierName = "K.C. Salt International Co.,Ltd",
            LotNumber = "26KC052501",
            MfgDate = new DateTime(2026, 5, 8),
            ReceiveDate = new DateTime(2026, 5, 10),
            UomId = Guid.NewGuid(),
            CreateBy = "test",
            CreateDate = DateTime.Now
        });
        context.LimsQaqcScans.Add(new LimsQaqcScanEntity
        {
            QaqcScanId = Guid.NewGuid(),
            ReceiptDetailId = receiptDetailId
        });

        foreach (var bodyItem in request.OcrResult.Single().BodyJson!.BodyItem)
        {
            var coaParameterId = Guid.NewGuid();
            var manageCoaParameterId = Guid.NewGuid();
            context.LimsCoaParameterMappings.Add(new LimsCoaParameterMappingEntity
            {
                CoaMappingParameterId = Guid.NewGuid(),
                OcrParameterName = bodyItem.ParameterName,
                CoaParameterName = bodyItem.ParameterName,
                IsActive = "YES",
                CreateBy = "test",
                CreateDate = DateTime.Now
            });
            context.LimsCoaParameters.Add(new LimsCoaParameterEntity
            {
                CoaParameterId = coaParameterId,
                CoaParameterName = bodyItem.ParameterName,
                IsActive = "YES"
            });
            context.LimsManageCoaParameters.Add(new LimsManageCoaParameterEntity
            {
                ManageCoaParameterId = manageCoaParameterId,
                ItemRmId = itemRmId,
                CoaParameterId = coaParameterId,
                IsActive = "YES"
            });
            context.LimsQaqcCoaParameterTests.Add(new LimsQaqcCoaParameterTestEntity
            {
                CoaParameterTestId = Guid.NewGuid(),
                ReceiptDetailId = receiptDetailId,
                ManageCoaParameterId = manageCoaParameterId,
                Result = "old",
                CreateBy = "test",
                CreateDate = DateTime.Now
            });
        }

        await context.SaveChangesAsync();
        return receiptDetailId;
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
