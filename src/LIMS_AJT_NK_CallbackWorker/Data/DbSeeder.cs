using LIMS_AJT_NK_CallbackWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_CallbackWorker.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(WorkerDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        if (await dbContext.LimsOcrConfigApis.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.LimsOcrConfigApis.Add(new LimsOcrConfigApiEntity
        {
            IsEnabled = true,
            InputOcrUrl = "http://localhost:5117/input_ocr",
            CallbackUrl = "http://localhost:5117/api/call_back",
            InboundDirectory = "1_Inbound",
            ProcessingDirectory = "2_Processing",
            SuccessDirectory = "3_Success",
            ErrorDirectory = "4_Error",
            DocumentHostDirectory = null,
            DocumentWebPath = "../_Documents",
            DocumentGroup = "QC_COA",
            FlowId = null,
            IntervalSeconds = 30,
            FileStableSeconds = 5,
            MaxSendAttempts = 3,
            RetryDelaySeconds = 5,
            RequestTimeoutSeconds = 60,
            IsInterface = false,
            CreateBy = "system",
            CreateDate = DateTime.Now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
