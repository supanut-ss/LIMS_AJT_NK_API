using LIMS_AJT_NK_CallbackWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_CallbackWorker.Data;

public class WorkerDbContext(DbContextOptions<WorkerDbContext> options) : DbContext(options)
{
    public DbSet<LimsOcrConfigApiEntity> LimsOcrConfigApis => Set<LimsOcrConfigApiEntity>();
    public DbSet<InterfaceLimsOcrLogEntity> InterfaceLimsOcrLogs => Set<InterfaceLimsOcrLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LimsOcrConfigApiEntity>(entity =>
        {
            entity.ToTable("t_interface_lims_ocr_config_api");
            entity.HasKey(x => x.ConfigId);

            entity.Property(x => x.ConfigId).HasColumnName("config_id").HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasColumnType("bit").HasDefaultValue(true);
            entity.Property(x => x.InputOcrUrl).HasColumnName("input_ocr_url").HasMaxLength(500).IsRequired();
            entity.Property(x => x.CallbackUrl).HasColumnName("callback_url").HasMaxLength(500);
            entity.Property(x => x.InboundDirectory).HasColumnName("inbound_directory").HasMaxLength(500).IsRequired();
            entity.Property(x => x.ProcessingDirectory).HasColumnName("processing_directory").HasMaxLength(500).IsRequired();
            entity.Property(x => x.SuccessDirectory).HasColumnName("success_directory").HasMaxLength(500).IsRequired();
            entity.Property(x => x.ErrorDirectory).HasColumnName("error_directory").HasMaxLength(500).IsRequired();
            entity.Property(x => x.FlowId).HasColumnName("flow_id").HasMaxLength(100);
            entity.Property(x => x.IntervalSeconds).HasColumnName("interval_seconds").HasDefaultValue(30);
            entity.Property(x => x.IsInterface).HasColumnName("is_interface").HasColumnType("bit").HasDefaultValue(false);
            entity.Property(x => x.CreateBy).HasColumnName("create_by").HasMaxLength(25);
            entity.Property(x => x.CreateDate).HasColumnName("create_date").HasColumnType("datetime").HasDefaultValueSql("GETDATE()");
        });

        modelBuilder.Entity<InterfaceLimsOcrLogEntity>(entity =>
        {
            entity.ToTable("t_interface_lims_ocr_log");
            entity.HasKey(x => x.LogId);

            entity.Property(x => x.LogId).HasColumnName("log_id").HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.ApiName).HasColumnName("api_name").HasMaxLength(100).IsRequired();
            entity.Property(x => x.RequestUrl).HasColumnName("request_url").HasMaxLength(500);
            entity.Property(x => x.FlowId).HasColumnName("flow_id").HasMaxLength(100);
            entity.Property(x => x.JobTaskId).HasColumnName("job_task_id").HasMaxLength(100);
            entity.Property(x => x.FilePath).HasColumnName("file_path").HasMaxLength(500);
            entity.Property(x => x.RequestPayload).HasColumnName("request_payload");
            entity.Property(x => x.ResponseStatusCode).HasColumnName("response_status_code");
            entity.Property(x => x.ResponsePayload).HasColumnName("response_payload");
            entity.Property(x => x.IsSuccess).HasColumnName("is_success").HasColumnType("bit").HasDefaultValue(false);
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
            entity.Property(x => x.SourceSystem).HasColumnName("source_system").HasMaxLength(50);
            entity.Property(x => x.WorkStatus).HasColumnName("work_status").HasMaxLength(50).HasDefaultValue("submitted");
            entity.Property(x => x.FinalPath).HasColumnName("final_path").HasMaxLength(500);
            entity.Property(x => x.CompletedDate).HasColumnName("completed_date").HasColumnType("datetime");
            entity.Property(x => x.IsInterface).HasColumnName("is_interface").HasColumnType("bit").HasDefaultValue(false);
            entity.Property(x => x.CreateBy).HasColumnName("create_by").HasMaxLength(25);
            entity.Property(x => x.CreateDate).HasColumnName("create_date").HasColumnType("datetime").HasDefaultValueSql("GETDATE()");

            entity.HasIndex(x => x.CreateDate);
            entity.HasIndex(x => x.JobTaskId);
            entity.HasIndex(x => x.FlowId);
        });
    }
}
