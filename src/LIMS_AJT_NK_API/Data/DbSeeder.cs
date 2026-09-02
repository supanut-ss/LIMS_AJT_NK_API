using LIMS_AJT_NK_API.Models;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_API.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        if (await dbContext.InterfaceLimsOcrConfigApis.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.InterfaceLimsOcrConfigApis.Add(new InterfaceLimsOcrConfigApiEntity
        {
            IsEnabled = true,
            InputOcrUrl = "http://localhost:5117/input_ocr",
            InputOcrFileUrl = "http://dev-hippo.ztrus.net:6206/aji/input_ocr",
            SubmissionMode = "file",
            InputOcrFileFieldName = "files",
            InputOcrBearerToken = null,
            UpdateMasterUrl = "http://dev-hippo.ztrus.net:6206/aji/update_master_data",
            GetResultOcrUrl = "http://dev-hippo.ztrus.net:6206/aji/get_result_ocr",
            FeedbackUrl = "http://dev-hippo.ztrus.net:6206/aji/feedback",
            CallbackUrl = null,
            InboundDirectory = "1_Inbound",
            ProcessingDirectory = "2_Processing",
            SuccessDirectory = "3_Success",
            ErrorDirectory = "4_Error",
            DocumentHostDirectory = null,
            DocumentWebPath = "../_Documents",
            DocumentGroup = "QC_COA",
            FlowId = "6a5efa97abaf97614454562a",
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
