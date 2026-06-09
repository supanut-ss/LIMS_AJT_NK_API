using LIMS_AJT_NK_API.Models;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_API.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<InterfaceLimsOcrCallbackEntity> InterfaceLimsOcrCallbacks => Set<InterfaceLimsOcrCallbackEntity>();
    public DbSet<InterfaceLimsOcrResultEntity> InterfaceLimsOcrResults => Set<InterfaceLimsOcrResultEntity>();
    public DbSet<InterfaceLimsOcrResultItemEntity> InterfaceLimsOcrResultItems => Set<InterfaceLimsOcrResultItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InterfaceLimsOcrCallbackEntity>(entity =>
        {
            entity.ToTable("t_interface_lims_ocr_callback");
            entity.HasKey(x => x.CallbackId);

            entity.Property(x => x.CallbackId).HasColumnName("callback_id").HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.JobTaskId).HasColumnName("job_task_id").HasMaxLength(100).IsRequired();
            entity.Property(x => x.IsInterface).HasColumnName("is_interface").HasColumnType("bit").HasDefaultValue(false);
            entity.Property(x => x.CreateBy).HasColumnName("create_by").HasMaxLength(25);
            entity.Property(x => x.CreateDate).HasColumnName("create_date").HasColumnType("datetime").HasDefaultValueSql("GETDATE()");

            entity.HasIndex(x => x.JobTaskId);
        });

        modelBuilder.Entity<InterfaceLimsOcrResultEntity>(entity =>
        {
            entity.ToTable("t_interface_lims_ocr_result");
            entity.HasKey(x => x.ResultId);

            entity.Property(x => x.ResultId).HasColumnName("result_id").HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.CallbackId).HasColumnName("callback_id").IsRequired();
            entity.Property(x => x.IsInterface).HasColumnName("is_interface").HasColumnType("bit").HasDefaultValue(false);
            entity.Property(x => x.PageId).HasColumnName("page_id").IsRequired();
            entity.Property(x => x.TrackingId).HasColumnName("tracking_id").HasMaxLength(100);
            entity.Property(x => x.TrackingStatus).HasColumnName("tracking_status").HasMaxLength(50).IsRequired();

            entity.Property(x => x.ProductName).HasColumnName("product_name").HasMaxLength(255);
            entity.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(100);
            entity.Property(x => x.SupplierName).HasColumnName("supplier_name").HasMaxLength(255);
            entity.Property(x => x.LotNumber).HasColumnName("lot_number").HasMaxLength(100);
            entity.Property(x => x.OriginSupplierName).HasColumnName("origin_supplier_name").HasMaxLength(255);
            entity.Property(x => x.OriginProductName).HasColumnName("origin_product_name").HasMaxLength(255);
            entity.Property(x => x.ExpiryDate).HasColumnName("expiry_date").HasColumnType("date");
            entity.Property(x => x.MfgDate).HasColumnName("mfg_date").HasColumnType("date");
            entity.Property(x => x.InternalLot).HasColumnName("internal_lot").HasMaxLength(50);
            entity.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("decimal(18,4)");

            entity.Property(x => x.CreateBy).HasColumnName("create_by").HasMaxLength(25);
            entity.Property(x => x.CreateDate).HasColumnName("create_date").HasColumnType("datetime").HasDefaultValueSql("GETDATE()");

            entity.HasIndex(x => new { x.CallbackId, x.PageId }).IsUnique();
            entity.HasIndex(x => x.CallbackId);
            entity.HasIndex(x => x.TrackingId);
            entity.HasIndex(x => x.TrackingStatus);
            entity.HasIndex(x => x.LotNumber);
            entity.HasIndex(x => x.ExpiryDate);

            entity.HasOne(x => x.Callback)
                .WithMany(x => x.Results)
                .HasForeignKey(x => x.CallbackId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InterfaceLimsOcrResultItemEntity>(entity =>
        {
            entity.ToTable("t_interface_lims_ocr_result_item");
            entity.HasKey(x => x.ItemId);

            entity.Property(x => x.ItemId).HasColumnName("item_id").HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.ResultId).HasColumnName("result_id").IsRequired();
            entity.Property(x => x.IsInterface).HasColumnName("is_interface").HasColumnType("bit").HasDefaultValue(false);
            entity.Property(x => x.Seq).HasColumnName("seq").IsRequired();
            entity.Property(x => x.ParameterName).HasColumnName("parameter_name").HasMaxLength(255).IsRequired();
            entity.Property(x => x.ResultValue).HasColumnName("result").HasMaxLength(255);
            entity.Property(x => x.Uom).HasColumnName("uom").HasMaxLength(25);

            entity.Property(x => x.CreateBy).HasColumnName("create_by").HasMaxLength(25);
            entity.Property(x => x.CreateDate).HasColumnName("create_date").HasColumnType("datetime").HasDefaultValueSql("GETDATE()");

            entity.HasIndex(x => new { x.ResultId, x.Seq }).IsUnique();
            entity.HasIndex(x => x.ResultId);
            entity.HasIndex(x => x.ParameterName);

            entity.HasOne(x => x.OcrResult)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.ResultId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}