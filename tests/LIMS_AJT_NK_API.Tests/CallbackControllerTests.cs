using System.Text.Json;
using LIMS_AJT_NK_API.Controllers;
using LIMS_AJT_NK_API.Models;
using LIMS_AJT_NK_API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LIMS_AJT_NK_API.Tests;

public class CallbackControllerTests
{
    [Fact]
    public async Task CallbackTest_AcceptsAnyJsonAndPersistsRawPayload()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = CreateController(
            database.Context,
            new StubInterfaceService(new OcrCallbackInterfaceSummary()));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.Request.Path = "/callback_test";
        using var json = JsonDocument.Parse("{\"ping\":\"test\"}");

        var result = await controller.CallbackTest(
            json.RootElement.Clone(),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var log = await database.Context.InterfaceLimsOcrLogs.SingleAsync();
        Assert.Equal("callback_test", log.ApiName);
        Assert.Equal("{\"ping\":\"test\"}", log.RequestPayload);
        Assert.True(log.IsSuccess);
    }

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

        Assert.NotNull(request!.Summary);
        Assert.Equal(1, request.Summary.Total);
        Assert.Equal("readytocheck", request.OcrResult.Single().Status);
        Assert.Equal(
            "certificate_of_analysis",
            request.OcrResult.Single().BodyJson!.DocumentClassification);

        var actionResult = await controller.ReceiveOcrResult(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(actionResult);
        database.Context.ChangeTracker.Clear();
        var callback = await database.Context.InterfaceLimsOcrCallbacks
            .Include(x => x.Results)
            .ThenInclude(x => x.Items)
            .SingleAsync();
        var result = Assert.Single(callback.Results);
        Assert.Equal(1, result.PageId);
        Assert.Equal("6a8283ddb3b7d074da97e8f1", result.FileId);
        Assert.Equal("readytocheck", result.Status);
        Assert.Equal("certificate_of_analysis", result.DocumentClassification);
        Assert.Equal("26KC052501", result.LotNumber);
        Assert.Equal("26KC052501", result.InternalLot);
        Assert.Equal(94000m, result.Quantity);
        Assert.Equal("kg", result.QuantityUom);
        Assert.Equal(new DateTime(2026, 5, 8), result.MfgDate);
        Assert.Equal(new DateTime(2029, 5, 8), result.ExpiryDate);
        Assert.Equal(9, result.Items.Count);
        Assert.Contains("\"product_name\":0.8824", result.ConfidenceJson);
        Assert.Contains("\"total\":1", callback.SourceSummaryJson);

        var callbackLog = await database.Context.InterfaceLimsOcrLogs.SingleAsync();
        Assert.Contains("\"summary\":", callbackLog.RequestPayload);
        Assert.Contains("\"status\":\"readytocheck\"", callbackLog.RequestPayload);
        Assert.Contains("\"document_classification\":\"certificate_of_analysis\"", callbackLog.RequestPayload);
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

        var replayResult = await controller.ReceiveOcrResult(request, CancellationToken.None);
        var replayOk = Assert.IsType<OkObjectResult>(replayResult);
        using var replayJson = JsonDocument.Parse(JsonSerializer.Serialize(replayOk.Value));
        Assert.True(replayJson.RootElement.GetProperty("data").GetProperty("is_duplicate").GetBoolean());
        Assert.Equal(9, replayJson.RootElement.GetProperty("data").GetProperty("updated_item_count").GetInt32());

        database.Context.ChangeTracker.Clear();
        Assert.Single(await database.Context.InterfaceLimsOcrCallbacks.ToListAsync());
        Assert.Equal(9, await database.Context.LimsQaqcCoaParameterTransactions.CountAsync());
        Assert.Equal(2, await database.Context.InterfaceLimsOcrLogs.CountAsync());
        Assert.Contains(
            await database.Context.InterfaceLimsOcrLogs.ToListAsync(),
            x => x.WorkStatus == "callback_replayed");
    }

    [Fact]
    public async Task ReceiveOcrResult_AllowsOneLogicalCallback_WhenDuplicatesArriveConcurrently()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "refined_salt_callback.json");
        var payload = await File.ReadAllTextAsync(fixturePath);
        var firstRequest = JsonSerializer.Deserialize<OcrCallbackRequest>(payload)!;
        var secondRequest = JsonSerializer.Deserialize<OcrCallbackRequest>(payload)!;
        await SeedRefinedSaltScenarioAsync(database.Context, firstRequest);
        database.Context.ChangeTracker.Clear();

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstController = CreateController(
            firstContext,
            new OcrCallbackInterfaceService(
                firstContext,
                NullLogger<OcrCallbackInterfaceService>.Instance));
        var secondController = CreateController(
            secondContext,
            new OcrCallbackInterfaceService(
                secondContext,
                NullLogger<OcrCallbackInterfaceService>.Instance));
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<IActionResult> SendAsync(CallbackController controller, OcrCallbackRequest request)
        {
            await start.Task;
            return await controller.ReceiveOcrResult(request, CancellationToken.None);
        }

        var firstTask = SendAsync(firstController, firstRequest);
        var secondTask = SendAsync(secondController, secondRequest);
        start.SetResult(true);
        var responses = await Task.WhenAll(firstTask, secondTask);

        var responseData = responses
            .Select(Assert.IsType<OkObjectResult>)
            .Select(x => JsonDocument.Parse(JsonSerializer.Serialize(x.Value)))
            .ToList();
        try
        {
            var callbackIds = responseData
                .Select(x => x.RootElement.GetProperty("data").GetProperty("callback_id").GetGuid())
                .Distinct()
                .ToList();
            Assert.Single(callbackIds);
            Assert.Single(
                responseData,
                x => x.RootElement.GetProperty("data").GetProperty("is_duplicate").GetBoolean());
            Assert.All(
                responseData,
                x => Assert.Equal(
                    9,
                    x.RootElement.GetProperty("data").GetProperty("updated_item_count").GetInt32()));
        }
        finally
        {
            foreach (var response in responseData)
            {
                response.Dispose();
            }
        }

        await using var verificationContext = database.CreateContext();
        Assert.Single(await verificationContext.InterfaceLimsOcrCallbacks.ToListAsync());
        Assert.Equal(9, await verificationContext.InterfaceLimsOcrResultItems.CountAsync());
        Assert.Equal(9, await verificationContext.LimsQaqcCoaParameterTransactions.CountAsync());
        Assert.Equal(2, await verificationContext.InterfaceLimsOcrLogs.CountAsync());
    }

    [Fact]
    public async Task ReceiveOcrResult_AcceptsOnePayloadAndConflictsTheOther_WhenDifferentPayloadsRace()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var summary = new OcrCallbackInterfaceSummary
        {
            InterfaceStatus = OcrInterfaceStatuses.NotMatched,
            SkippedItemCount = 1
        };
        var firstController = CreateController(firstContext, new StubInterfaceService(summary));
        var secondController = CreateController(secondContext, new StubInterfaceService(summary));
        var firstRequest = CreateRequest();
        var secondRequest = CreateRequest();
        secondRequest.OcrResult.Single().BodyJson!.BodyItem.Single().Result = "different";
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<IActionResult> SendAsync(CallbackController controller, OcrCallbackRequest request)
        {
            await start.Task;
            return await controller.ReceiveOcrResult(request, CancellationToken.None);
        }

        var firstTask = SendAsync(firstController, firstRequest);
        var secondTask = SendAsync(secondController, secondRequest);
        start.SetResult(true);
        var responses = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(responses, x => x is OkObjectResult);
        Assert.Single(responses, x => x is ConflictObjectResult);
        await using var verificationContext = database.CreateContext();
        Assert.Single(await verificationContext.InterfaceLimsOcrCallbacks.ToListAsync());
        Assert.Single(await verificationContext.InterfaceLimsOcrResultItems.ToListAsync());
        Assert.Empty(await verificationContext.LimsQaqcCoaParameterTransactions.ToListAsync());
        Assert.Contains(
            await verificationContext.InterfaceLimsOcrLogs.ToListAsync(),
            x => x.WorkStatus == "callback_conflict");
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
            Assert.Equal("stored", secondPage.GetProperty("document_status").GetString());
            Assert.True(secondResponseJson.RootElement.GetProperty("data").GetProperty("is_duplicate").GetBoolean());
            Assert.Equal(9, await database.Context.LimsQaqcCoaParameterTransactions.CountAsync());
            Assert.Single(await database.Context.InterfaceLimsOcrCallbacks.ToListAsync());
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

        var retryResult = await controller.ReceiveOcrResult(CreateRequest(), CancellationToken.None);
        Assert.Equal(500, Assert.IsType<ObjectResult>(retryResult).StatusCode);
        database.Context.ChangeTracker.Clear();
        Assert.Single(await database.Context.InterfaceLimsOcrCallbacks.ToListAsync());
        Assert.Equal(2, await database.Context.InterfaceLimsOcrLogs.CountAsync());
        Assert.All(
            await database.Context.InterfaceLimsOcrLogs.ToListAsync(),
            retryLog => Assert.Equal("callback_error", retryLog.WorkStatus));
    }

    [Fact]
    public async Task ReceiveOcrResult_ReportsProcessedExisting_WhenPendingCallbackRetrySucceeds()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = CreateController(
            database.Context,
            new FailOnceInterfaceService(new OcrCallbackInterfaceSummary
            {
                InterfaceStatus = OcrInterfaceStatuses.Completed,
                UpdatedItemCount = 1
            }));

        Assert.Equal(
            500,
            Assert.IsType<ObjectResult>(
                await controller.ReceiveOcrResult(CreateRequest(), CancellationToken.None)).StatusCode);
        var retry = Assert.IsType<OkObjectResult>(
            await controller.ReceiveOcrResult(CreateRequest(), CancellationToken.None));

        using var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(retry.Value));
        var responseData = responseJson.RootElement.GetProperty("data");
        Assert.True(responseData.GetProperty("is_duplicate").GetBoolean());
        Assert.Equal("processed_existing", responseData.GetProperty("idempotency_status").GetString());
        Assert.Single(await database.Context.InterfaceLimsOcrCallbacks.ToListAsync());
        Assert.Contains(
            await database.Context.InterfaceLimsOcrLogs.ToListAsync(),
            x => x.WorkStatus == "callback_processed_existing");
    }

    [Fact]
    public async Task ReceiveOcrResult_Returns409_WhenJobTaskIdIsReusedWithDifferentPayload()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = CreateController(
            database.Context,
            new StubInterfaceService(new OcrCallbackInterfaceSummary
            {
                InterfaceStatus = OcrInterfaceStatuses.NotMatched,
                SkippedItemCount = 1
            }));
        var firstRequest = CreateRequest();
        var changedRequest = CreateRequest();
        changedRequest.OcrResult.Single().BodyJson!.BodyItem.Single().Result = "DIFFERENT";

        Assert.IsType<OkObjectResult>(
            await controller.ReceiveOcrResult(firstRequest, CancellationToken.None));
        var conflictResult = await controller.ReceiveOcrResult(changedRequest, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(conflictResult);
        using var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(conflict.Value));
        Assert.Equal("job_task_id", responseJson.RootElement.GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Single(await database.Context.InterfaceLimsOcrCallbacks.ToListAsync());
        Assert.Contains(
            await database.Context.InterfaceLimsOcrLogs.ToListAsync(),
            x => x.WorkStatus == "callback_conflict");
    }

    [Fact]
    public async Task ReceiveOcrResult_ReusesCallback_WhenNormalizedPayloadIsEquivalent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = CreateController(
            database.Context,
            new StubInterfaceService(new OcrCallbackInterfaceSummary
            {
                InterfaceStatus = OcrInterfaceStatuses.NotMatched,
                SkippedItemCount = 1
            }));
        var firstRequest = CreateRequest();
        firstRequest.JobTaskId = "  test-job  ";
        firstRequest.OcrResult.Single().BodyJson!.ProductName = "  Product  ";
        firstRequest.OcrResult.Single().BodyJson!.BodyItem.Single().ParameterName = "  Moisture  ";
        var equivalentRequest = CreateRequest();

        var firstResult = Assert.IsType<OkObjectResult>(
            await controller.ReceiveOcrResult(firstRequest, CancellationToken.None));
        var replayResult = Assert.IsType<OkObjectResult>(
            await controller.ReceiveOcrResult(equivalentRequest, CancellationToken.None));

        using var firstJson = JsonDocument.Parse(JsonSerializer.Serialize(firstResult.Value));
        using var replayJson = JsonDocument.Parse(JsonSerializer.Serialize(replayResult.Value));
        Assert.Equal(
            firstJson.RootElement.GetProperty("data").GetProperty("callback_id").GetGuid(),
            replayJson.RootElement.GetProperty("data").GetProperty("callback_id").GetGuid());
        Assert.True(replayJson.RootElement.GetProperty("data").GetProperty("is_duplicate").GetBoolean());
        Assert.Single(await database.Context.InterfaceLimsOcrCallbacks.ToListAsync());
    }

    [Fact]
    public async Task ReceiveOcrResult_TreatsJobTaskIdCaseAsSignificant()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = CreateController(
            database.Context,
            new StubInterfaceService(new OcrCallbackInterfaceSummary
            {
                InterfaceStatus = OcrInterfaceStatuses.NotMatched,
                SkippedItemCount = 1
            }));
        var lowerCaseRequest = CreateRequest();
        var upperCaseRequest = CreateRequest();
        upperCaseRequest.JobTaskId = "TEST-JOB";

        Assert.IsType<OkObjectResult>(
            await controller.ReceiveOcrResult(lowerCaseRequest, CancellationToken.None));
        Assert.IsType<OkObjectResult>(
            await controller.ReceiveOcrResult(upperCaseRequest, CancellationToken.None));

        Assert.Equal(2, await database.Context.InterfaceLimsOcrCallbacks.CountAsync());
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

    private sealed class FailOnceInterfaceService(OcrCallbackInterfaceSummary summary)
        : IOcrCallbackInterfaceService
    {
        private int attemptCount;

        public Task<OcrCallbackInterfaceSummary> ProcessAsync(
            Guid callbackId,
            CancellationToken cancellationToken = default)
        {
            return Interlocked.Increment(ref attemptCount) == 1
                ? Task.FromException<OcrCallbackInterfaceSummary>(
                    new InvalidOperationException("Simulated first-attempt failure"))
                : Task.FromResult(summary);
        }
    }

}
