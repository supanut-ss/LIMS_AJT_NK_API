using LIMS_AJT_NK_API.Data;
using LIMS_AJT_NK_API.Models;
using LIMS_AJT_NK_API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LIMS_AJT_NK_API.Tests;

public class OcrCallbackInterfaceServiceTests
{
    [Fact]
    public async Task ProcessAsync_UpdatesResultAuditAndInterfaceFlags_WhenAllDataMatches()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(database.Context);

        var summary = await CreateService(database.Context).ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.Completed, summary.InterfaceStatus);
        Assert.Equal(1, summary.UpdatedItemCount);
        Assert.Equal(0, summary.SkippedItemCount);

        database.Context.ChangeTracker.Clear();
        var test = await database.Context.LimsQaqcCoaParameterTests.SingleAsync();
        var transaction = await database.Context.LimsQaqcCoaParameterTransactions.SingleAsync();
        var callback = await database.Context.InterfaceLimsOcrCallbacks
            .Include(x => x.Results)
            .ThenInclude(x => x.Items)
            .SingleAsync();

        Assert.Equal("49.2", test.Result);
        Assert.Equal("api", test.UpdateBy);
        Assert.NotNull(test.UpdateDate);
        Assert.Equal("OCR Callback", transaction.LogType);
        Assert.Equal("49.2", transaction.Result);
        Assert.Equal(scenario.ReceiptDetailId, transaction.ReceiptDetailId);
        Assert.Equal("api", transaction.TransationBy);
        Assert.True(callback.IsInterface);
        Assert.True(callback.Results.Single().IsInterface);
        Assert.True(callback.Results.Single().Items.Single().IsInterface);
    }

    [Fact]
    public async Task ProcessAsync_MatchesTrimmedTextWithoutCaseSensitivity()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(
            database.Context,
            productName: "  sample product  ",
            supplierName: "  sample supplier  ",
            lotNumber: "  lot-001  ",
            parameterName: "  moisture  ");

        var summary = await CreateService(database.Context).ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.Completed, summary.InterfaceStatus);
        Assert.Equal(1, summary.UpdatedItemCount);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsNotMatched_WhenInboundDoesNotMatchAllFourFields()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(database.Context, lotNumber: "DIFFERENT-LOT");

        var summary = await CreateService(database.Context).ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.NotMatched, summary.InterfaceStatus);
        Assert.Equal("inbound_not_found", summary.Pages.Single().Reason);
        Assert.Empty(database.Context.LimsQaqcCoaParameterTransactions);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsNotMatched_WhenInboundMatchIsAmbiguous()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(database.Context);
        var original = await database.Context.LimsInboundReceives.SingleAsync();
        database.Context.LimsInboundReceives.Add(new LimsInboundReceiveEntity
        {
            ReceiptDetailId = Guid.NewGuid(),
            ItemMasterId = original.ItemMasterId,
            SupplierName = original.SupplierName,
            LotNumber = original.LotNumber,
            MfgDate = original.MfgDate,
            CreateBy = "test",
            CreateDate = DateTime.Now
        });
        await database.Context.SaveChangesAsync();

        var summary = await CreateService(database.Context).ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.NotMatched, summary.InterfaceStatus);
        Assert.Equal("inbound_ambiguous", summary.Pages.Single().Reason);
        Assert.Empty(database.Context.LimsQaqcCoaParameterTransactions);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsNotMatched_WhenQaqcScanDoesNotExist()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(database.Context, includeScan: false);

        var summary = await CreateService(database.Context).ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.NotMatched, summary.InterfaceStatus);
        Assert.Equal("qaqc_scan_not_found", summary.Pages.Single().Reason);
        Assert.Empty(database.Context.LimsQaqcCoaParameterTransactions);
    }

    [Fact]
    public async Task ProcessAsync_CommitsValidItemsAndReportsPartial_WhenOneMappingIsMissing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(database.Context, includeUnmappedItem: true);

        var summary = await CreateService(database.Context).ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.Partial, summary.InterfaceStatus);
        Assert.Equal(1, summary.UpdatedItemCount);
        Assert.Equal(1, summary.SkippedItemCount);
        Assert.Equal("mapping_not_found", summary.Pages.Single().Items.Single(x => x.Status == "skipped").Reason);

        database.Context.ChangeTracker.Clear();
        var callback = await database.Context.InterfaceLimsOcrCallbacks
            .Include(x => x.Results)
            .ThenInclude(x => x.Items)
            .SingleAsync();
        Assert.False(callback.IsInterface);
        Assert.False(callback.Results.Single().IsInterface);
        Assert.Single(callback.Results.Single().Items, x => x.IsInterface);
    }

    [Fact]
    public async Task ProcessAsync_SkipsItem_WhenMappingIsInactive()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(database.Context);
        var mapping = await database.Context.LimsCoaParameterMappings.SingleAsync();
        mapping.IsActive = "NO";
        await database.Context.SaveChangesAsync();

        var summary = await CreateService(database.Context).ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.NotMatched, summary.InterfaceStatus);
        Assert.Equal("mapping_not_found", summary.Pages.Single().Items.Single().Reason);
    }

    [Fact]
    public async Task ProcessAsync_SkipsItem_WhenParameterTestDoesNotExist()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(database.Context);
        database.Context.LimsQaqcCoaParameterTests.Remove(
            await database.Context.LimsQaqcCoaParameterTests.SingleAsync());
        await database.Context.SaveChangesAsync();

        var summary = await CreateService(database.Context).ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.NotMatched, summary.InterfaceStatus);
        Assert.Equal("parameter_test_not_found", summary.Pages.Single().Items.Single().Reason);
    }

    [Fact]
    public async Task ProcessAsync_SkipsItem_WhenResultIsMissing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(database.Context);
        var item = await database.Context.InterfaceLimsOcrResultItems.SingleAsync();
        item.ResultValue = null;
        await database.Context.SaveChangesAsync();

        var summary = await CreateService(database.Context).ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.NotMatched, summary.InterfaceStatus);
        Assert.Equal("result_missing", summary.Pages.Single().Items.Single().Reason);
    }

    [Fact]
    public async Task ProcessAsync_IgnoresPagesThatAreNotReadyToCheck()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(database.Context, trackingStatus: "processing");

        var summary = await CreateService(database.Context).ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.NotMatched, summary.InterfaceStatus);
        Assert.Equal("ignored", summary.Pages.Single().Status);
        Assert.Equal(0, summary.UpdatedItemCount);
        Assert.Empty(database.Context.LimsQaqcCoaParameterTransactions);
    }

    [Fact]
    public async Task ProcessAsync_ProcessesCallbacksWithTheSameJobTaskIdEveryTime()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = await SeedScenarioAsync(database.Context, jobTaskId: "duplicate-job");
        var secondCallbackId = await AddCallbackAsync(database.Context, first, "duplicate-job");
        var service = CreateService(database.Context);

        var firstSummary = await service.ProcessAsync(first.CallbackId);
        var secondSummary = await service.ProcessAsync(secondCallbackId);

        Assert.Equal(OcrInterfaceStatuses.Completed, firstSummary.InterfaceStatus);
        Assert.Equal(OcrInterfaceStatuses.Completed, secondSummary.InterfaceStatus);
        Assert.Equal(2, await database.Context.LimsQaqcCoaParameterTransactions.CountAsync());
    }

    [Fact]
    public async Task ProcessAsync_ResetsInterfaceFlags_WhenCallbackIsReprocessedAndNoLongerMatches()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scenario = await SeedScenarioAsync(database.Context);
        var service = CreateService(database.Context);

        var firstSummary = await service.ProcessAsync(scenario.CallbackId);
        Assert.Equal(OcrInterfaceStatuses.Completed, firstSummary.InterfaceStatus);

        var mapping = await database.Context.LimsCoaParameterMappings.SingleAsync();
        mapping.IsActive = "NO";
        await database.Context.SaveChangesAsync();

        var secondSummary = await service.ProcessAsync(scenario.CallbackId);

        Assert.Equal(OcrInterfaceStatuses.NotMatched, secondSummary.InterfaceStatus);
        database.Context.ChangeTracker.Clear();
        var callback = await database.Context.InterfaceLimsOcrCallbacks
            .Include(x => x.Results)
            .ThenInclude(x => x.Items)
            .SingleAsync();
        Assert.False(callback.IsInterface);
        Assert.False(callback.Results.Single().IsInterface);
        Assert.False(callback.Results.Single().Items.Single().IsInterface);
        Assert.Single(await database.Context.LimsQaqcCoaParameterTransactions.ToListAsync());
    }

    [Fact]
    public async Task ProcessAsync_RollsBackQaqcWritesButKeepsRawCallback_WhenSaveFails()
    {
        await using var database = await TestDatabase.CreateAsync(useFailingContext: true);
        var scenario = await SeedScenarioAsync(database.Context);
        ((FailingApplicationDbContext)database.Context).FailAfterSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(database.Context).ProcessAsync(scenario.CallbackId));

        await using var verificationContext = database.CreateContext();
        Assert.Equal("old", (await verificationContext.LimsQaqcCoaParameterTests.SingleAsync()).Result);
        Assert.Empty(verificationContext.LimsQaqcCoaParameterTransactions);
        Assert.Equal(scenario.CallbackId, (await verificationContext.InterfaceLimsOcrCallbacks.SingleAsync()).CallbackId);
    }

    private static OcrCallbackInterfaceService CreateService(ApplicationDbContext context)
    {
        return new OcrCallbackInterfaceService(
            context,
            NullLogger<OcrCallbackInterfaceService>.Instance);
    }

    private static async Task<Scenario> SeedScenarioAsync(
        ApplicationDbContext context,
        bool includeScan = true,
        bool includeUnmappedItem = false,
        string trackingStatus = "ReadyToCheck",
        string productName = "Sample Product",
        string supplierName = "Sample Supplier",
        string lotNumber = "LOT-001",
        string parameterName = "Moisture",
        string jobTaskId = "job-001")
    {
        var receiptDetailId = Guid.NewGuid();
        var itemMasterId = Guid.NewGuid();
        var coaParameterId = Guid.NewGuid();
        var manageCoaParameterId = Guid.NewGuid();
        var callbackId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var receiveDate = new DateTime(2026, 1, 2);

        context.WmsItems.Add(new WmsItemEntity
        {
            ItemMasterId = itemMasterId,
            Description = "Sample Product"
        });
        context.LimsInboundReceives.Add(new LimsInboundReceiveEntity
        {
            ReceiptDetailId = receiptDetailId,
            ItemMasterId = itemMasterId,
            SupplierName = "Sample Supplier",
            LotNumber = "LOT-001",
            MfgDate = new DateTime(2026, 1, 1),
            ReceiveDate = receiveDate,
            UomId = Guid.NewGuid(),
            CreateBy = "test",
            CreateDate = DateTime.Now
        });

        if (includeScan)
        {
            context.LimsQaqcScans.Add(new LimsQaqcScanEntity
            {
                QaqcScanId = Guid.NewGuid(),
                ReceiptDetailId = receiptDetailId
            });
        }

        context.LimsCoaParameterMappings.Add(new LimsCoaParameterMappingEntity
        {
            CoaMappingParameterId = Guid.NewGuid(),
            CoaParameterName = "Moisture Content",
            OcrParameterName = "Moisture",
            IsActive = "YES",
            CreateBy = "test",
            CreateDate = DateTime.Now
        });
        context.LimsCoaParameters.Add(new LimsCoaParameterEntity
        {
            CoaParameterId = coaParameterId,
            CoaParameterName = "Moisture Content",
            IsActive = "YES"
        });
        context.LimsManageCoaParameters.Add(new LimsManageCoaParameterEntity
        {
            ManageCoaParameterId = manageCoaParameterId,
            ItemRmId = Guid.NewGuid(),
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

        var callback = CreateCallback(
            callbackId,
            resultId,
            receiptDetailId,
            jobTaskId,
            trackingStatus,
            productName,
            supplierName,
            lotNumber,
            parameterName,
            includeUnmappedItem);
        context.InterfaceLimsOcrCallbacks.Add(callback);
        await context.SaveChangesAsync();

        return new Scenario(callbackId, receiptDetailId, resultId);
    }

    private static async Task<Guid> AddCallbackAsync(
        ApplicationDbContext context,
        Scenario scenario,
        string jobTaskId)
    {
        var callbackId = Guid.NewGuid();
        context.InterfaceLimsOcrCallbacks.Add(CreateCallback(
            callbackId,
            Guid.NewGuid(),
            scenario.ReceiptDetailId,
            jobTaskId,
            "ReadyToCheck",
            "Sample Product",
            "Sample Supplier",
            "LOT-001",
            "Moisture",
            false));
        await context.SaveChangesAsync();
        return callbackId;
    }

    private static InterfaceLimsOcrCallbackEntity CreateCallback(
        Guid callbackId,
        Guid resultId,
        Guid receiptDetailId,
        string jobTaskId,
        string trackingStatus,
        string productName,
        string supplierName,
        string lotNumber,
        string parameterName,
        bool includeUnmappedItem)
    {
        var callback = new InterfaceLimsOcrCallbackEntity
        {
            CallbackId = callbackId,
            JobTaskId = jobTaskId,
            CreateBy = "test",
            CreateDate = DateTime.Now
        };
        var result = new InterfaceLimsOcrResultEntity
        {
            ResultId = resultId,
            Callback = callback,
            PageId = 1,
            TrackingStatus = trackingStatus,
            ProductName = productName,
            SupplierName = supplierName,
            LotNumber = lotNumber,
            MfgDate = new DateTime(2026, 1, 1),
            CreateBy = "test",
            CreateDate = DateTime.Now
        };
        result.Items.Add(new InterfaceLimsOcrResultItemEntity
        {
            ItemId = Guid.NewGuid(),
            OcrResult = result,
            Seq = 1,
            ParameterName = parameterName,
            ResultValue = "49.2",
            CreateBy = "test",
            CreateDate = DateTime.Now
        });
        if (includeUnmappedItem)
        {
            result.Items.Add(new InterfaceLimsOcrResultItemEntity
            {
                ItemId = Guid.NewGuid(),
                OcrResult = result,
                Seq = 2,
                ParameterName = "Unmapped",
                ResultValue = "10",
                CreateBy = "test",
                CreateDate = DateTime.Now
            });
        }

        callback.Results.Add(result);
        return callback;
    }

    private sealed record Scenario(Guid CallbackId, Guid ReceiptDetailId, Guid ResultId);

}
