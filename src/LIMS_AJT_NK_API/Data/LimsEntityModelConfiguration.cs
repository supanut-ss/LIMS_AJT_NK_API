using LIMS_AJT_NK_API.Models;
using Microsoft.EntityFrameworkCore;

namespace LIMS_AJT_NK_API.Data;

internal static class LimsEntityModelConfiguration
{
    public static void ConfigureLimsEntities(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LimsCoaParameterMappingEntity>(entity =>
        {
            entity.ToTable("t_lims_coa_parameter_mapping");
            entity.HasKey(x => x.CoaMappingParameterId).HasName("PK_t_lims_coa_parameter_mapping");

            entity.Property(x => x.CoaMappingParameterId).HasColumnName("coa_mapping_parameter_id").ValueGeneratedNever();
            entity.Property(x => x.CoaParameterName).HasColumnName("coa_parameter_name").HasMaxLength(100);
            entity.Property(x => x.OcrParameterName).HasColumnName("ocr_parameter_name").HasMaxLength(100);
            entity.Property(x => x.IsActive).HasColumnName("is_active").HasMaxLength(3).IsRequired();
            entity.Property(x => x.CreateBy).HasColumnName("create_by").HasMaxLength(25).IsRequired();
            entity.Property(x => x.CreateDate).HasColumnName("create_date").HasColumnType("datetime").IsRequired();
            entity.Property(x => x.UpdateBy).HasColumnName("update_by").HasMaxLength(25);
            entity.Property(x => x.UpdateDate).HasColumnName("update_date").HasColumnType("datetime");
            entity.Property(x => x.RowVersion).HasColumnName("rowversion").IsRowVersion();
        });

        modelBuilder.Entity<WmsItemEntity>(entity =>
        {
            entity.ToTable("t_wms_item");
            entity.HasKey(x => x.ItemMasterId);

            entity.Property(x => x.ItemMasterId).HasColumnName("item_master_id");
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(200);
        });

        modelBuilder.Entity<LimsCoaParameterEntity>(entity =>
        {
            entity.ToTable("t_lims_coa_parameter");
            entity.HasKey(x => x.CoaParameterId);

            entity.Property(x => x.CoaParameterId).HasColumnName("coa_parameter_id");
            entity.Property(x => x.CoaParameterName).HasColumnName("coa_parameter_name").HasMaxLength(100);
            entity.Property(x => x.IsActive).HasColumnName("is_active").HasMaxLength(3).IsRequired();
        });

        modelBuilder.Entity<LimsManageCoaParameterEntity>(entity =>
        {
            entity.ToTable("t_lims_manage_coa_parameter");
            entity.HasKey(x => x.ManageCoaParameterId);

            entity.Property(x => x.ManageCoaParameterId).HasColumnName("manage_coa_parameter_id");
            entity.Property(x => x.ItemRmId).HasColumnName("item_rm_id");
            entity.Property(x => x.CoaParameterId).HasColumnName("coa_parameter_id");
            entity.Property(x => x.IsActive).HasColumnName("is_active").HasMaxLength(3).IsRequired();
        });

        modelBuilder.Entity<LimsInboundReceiveEntity>(entity =>
        {
            entity.ToTable("t_lims_inbound_receive");
            entity.HasKey(x => x.ReceiptDetailId).HasName("PK_t_lims_inbound_receive");

            entity.Property(x => x.ReceiptDetailId).HasColumnName("receipt_detail_id").HasDefaultValueSql("NEWID()");
            entity.Property(x => x.WhMasterId).HasColumnName("wh_master_id");
            entity.Property(x => x.WhId).HasColumnName("wh_id").HasMaxLength(40);
            entity.Property(x => x.SupplierId).HasColumnName("supplier_id");
            entity.Property(x => x.SupplierCode).HasColumnName("supplier_code").HasMaxLength(50);
            entity.Property(x => x.InboundOrderNumber).HasColumnName("inbound_order_number").HasMaxLength(50);
            entity.Property(x => x.ItemMasterId).HasColumnName("item_master_id");
            entity.Property(x => x.ItemNumber).HasColumnName("item_number").HasMaxLength(50);
            entity.Property(x => x.QtyReceived).HasColumnName("qty_received").HasColumnType("float");
            entity.Property(x => x.UomId).HasColumnName("uom_id");
            entity.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(50);
            entity.Property(x => x.LotNumber).HasColumnName("lot_number").HasMaxLength(50);
            entity.Property(x => x.ExpiryDate).HasColumnName("expiry_date").HasMaxLength(8);
            entity.Property(x => x.ReceiveDate).HasColumnName("receive_date").HasColumnType("date");
            entity.Property(x => x.MfgDate).HasColumnName("mfg_date").HasColumnType("date");
            entity.Property(x => x.ExternalLot).HasColumnName("external_lot").HasMaxLength(50);
            entity.Property(x => x.Appearance).HasColumnName("appearance").HasColumnType("bit");
            entity.Property(x => x.Contaminant).HasColumnName("contaminant").HasColumnType("bit");
            entity.Property(x => x.Metal).HasColumnName("metal").HasColumnType("bit");
            entity.Property(x => x.Remark).HasColumnName("remark").HasMaxLength(500);
            entity.Property(x => x.CreateDate).HasColumnName("create_date").HasColumnType("datetime").HasDefaultValueSql("GETDATE()");
            entity.Property(x => x.CreateBy).HasColumnName("create_by").HasMaxLength(25).IsRequired();
            entity.Property(x => x.UpdateDate).HasColumnName("update_date").HasColumnType("datetime").HasDefaultValueSql("GETDATE()");
            entity.Property(x => x.UpdateBy).HasColumnName("update_by").HasMaxLength(25);
            entity.Property(x => x.ApproveStatus).HasColumnName("approve_status").HasMaxLength(25);
            entity.Property(x => x.ApproveBy).HasColumnName("approve_by").HasMaxLength(25);
            entity.Property(x => x.ApproveDate).HasColumnName("approve_date").HasColumnType("datetime");
            entity.Property(x => x.ApproveRemark).HasColumnName("approve_remark").HasMaxLength(500);
            entity.Property(x => x.SupervisorSubmitStatus).HasColumnName("supervisor_submit_status").HasMaxLength(25);
            entity.Property(x => x.SupervisorSubmitBy).HasColumnName("supervisor_submit_by").HasMaxLength(25);
            entity.Property(x => x.SupervisorSubmitDate).HasColumnName("supervisor_submit_date").HasColumnType("datetime");
            entity.Property(x => x.SupervisorSubmitRemark).HasColumnName("supervisor_submit_remark").HasMaxLength(500);
            entity.Property(x => x.QmrReviewStatus).HasColumnName("qmr_review_status").HasMaxLength(25);
            entity.Property(x => x.QmrReviewBy).HasColumnName("qmr_review_by").HasMaxLength(25);
            entity.Property(x => x.QmrReviewDate).HasColumnName("qmr_review_date").HasColumnType("datetime");
            entity.Property(x => x.QmrReviewRemark).HasColumnName("qmr_review_remark").HasMaxLength(500);
            entity.Property(x => x.RefExLot).HasColumnName("ref_ex_lot").HasMaxLength(25);
            entity.Property(x => x.SupplierName).HasColumnName("supplier_name").HasMaxLength(200);
            entity.Property(x => x.ItemRmCode).HasColumnName("item_rm_code").HasMaxLength(50);
            entity.Property(x => x.ItemRmName).HasColumnName("item_rm_name").HasMaxLength(500);

            entity.HasIndex(x => new { x.ReceiptDetailId, x.SupervisorSubmitStatus, x.ItemMasterId, x.SupplierId, x.CreateDate })
                .HasDatabaseName("idx_lims_inbound_receive");
        });

        modelBuilder.Entity<LimsQaqcScanEntity>(entity =>
        {
            entity.ToTable("t_lims_qaqc_scan");
            entity.HasKey(x => x.QaqcScanId).HasName("PK_t_lims_qaqc_scan");

            entity.Property(x => x.QaqcScanId).HasColumnName("qaqc_scan_id").ValueGeneratedNever();
            entity.Property(x => x.ReceiptDetailId).HasColumnName("receipt_detail_id").HasDefaultValueSql("NEWID()");
            entity.Property(x => x.ItemNumber).HasColumnName("item_number").HasMaxLength(50);
            entity.Property(x => x.ItemRmCode).HasColumnName("item_rm_code").HasMaxLength(50);
            entity.Property(x => x.ItemRmName).HasColumnName("item_rm_name").HasMaxLength(500);
            entity.Property(x => x.LotNumber).HasColumnName("lot_number").HasMaxLength(50);
            entity.Property(x => x.ExpiryDate).HasColumnName("expiry_date").HasMaxLength(8);
            entity.Property(x => x.MfgDate).HasColumnName("mfg_date").HasColumnType("date");
            entity.Property(x => x.LimsScaned).HasColumnName("lims_scaned").HasMaxLength(100);
            entity.Property(x => x.QcCenterStatus).HasColumnName("qc_center_status").HasMaxLength(15).HasDefaultValue("NONE");
            entity.Property(x => x.QcCenterScanBy).HasColumnName("qc_center_scan_by").HasMaxLength(50);
            entity.Property(x => x.QcCenterScanDate).HasColumnName("qc_center_scan_date").HasColumnType("datetime");
            entity.Property(x => x.QcCoaStatus).HasColumnName("qc_coa_status").HasMaxLength(15).HasDefaultValue("NONE");
            entity.Property(x => x.QcCoaBy).HasColumnName("qc_coa_by").HasMaxLength(50);
            entity.Property(x => x.QcCoaDate).HasColumnName("qc_coa_date").HasColumnType("datetime");
            entity.Property(x => x.QcCoaRemark).HasColumnName("qc_coa_remark").HasMaxLength(500);
            entity.Property(x => x.QaCoaStatus).HasColumnName("qa_coa_status").HasMaxLength(15).HasDefaultValue("NONE");
            entity.Property(x => x.QaCoaBy).HasColumnName("qa_coa_by").HasMaxLength(50);
            entity.Property(x => x.QaCoaDate).HasColumnName("qa_coa_date").HasColumnType("datetime");
            entity.Property(x => x.QaCoaRemark).HasColumnName("qa_coa_remark").HasMaxLength(500);
            entity.Property(x => x.QcSupervisorStatus).HasColumnName("qc_supervisor_status").HasMaxLength(15).HasDefaultValue("NONE");
            entity.Property(x => x.QcSupervisorBy).HasColumnName("qc_supervisor_by").HasMaxLength(50);
            entity.Property(x => x.QcSupervisorDate).HasColumnName("qc_supervisor_date").HasColumnType("datetime");
            entity.Property(x => x.QcSupervisorRemark).HasColumnName("qc_supervisor_remark").HasMaxLength(500);
            entity.Property(x => x.QaSupervisorStatus).HasColumnName("qa_supervisor_status").HasMaxLength(15).HasDefaultValue("NONE");
            entity.Property(x => x.QaSupervisorBy).HasColumnName("qa_supervisor_by").HasMaxLength(50);
            entity.Property(x => x.QaSupervisorDate).HasColumnName("qa_supervisor_date").HasColumnType("datetime");
            entity.Property(x => x.QaSupervisorRemark).HasColumnName("qa_supervisor_remark").HasMaxLength(500);
            entity.Property(x => x.MicrobioPlateNo).HasColumnName("microbio_plate_no");
            entity.Property(x => x.IsHold).HasColumnName("is_hold").HasColumnType("bit").HasDefaultValue(false);
            entity.Property(x => x.IsUnhold).HasColumnName("is_unhold").HasColumnType("bit").HasDefaultValue(false);

            entity.HasIndex(x => new { x.ReceiptDetailId, x.QaqcScanId }).HasDatabaseName("idx_lims_qaqc_scan");
        });

        modelBuilder.Entity<LimsDocumentEntity>(entity =>
        {
            entity.ToTable("t_lims_documents");
            entity.HasKey(x => x.DocumentId).HasName("PK_t_lims_documents_1");

            entity.Property(x => x.DocumentId).HasColumnName("document_id").UseIdentityColumn();
            entity.Property(x => x.PkId).HasColumnName("pk_id").IsRequired();
            entity.Property(x => x.DocumentGroup).HasColumnName("document_group").HasMaxLength(64);
            entity.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(256);
            entity.Property(x => x.DocumentCode).HasColumnName("document_code").HasMaxLength(64);
            entity.Property(x => x.DocumentName).HasColumnName("document_name").HasMaxLength(256);
            entity.Property(x => x.DocumentPath).HasColumnName("document_path").HasMaxLength(1024);
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(1024);
            entity.Property(x => x.Udf1).HasColumnName("udf1").HasMaxLength(512);
            entity.Property(x => x.Udf2).HasColumnName("udf2").HasMaxLength(512);
            entity.Property(x => x.Udf3).HasColumnName("udf3").HasMaxLength(512);
            entity.Property(x => x.IsActive).HasColumnName("is_active").HasColumnType("bit").HasDefaultValue(true);
            entity.Property(x => x.CreateBy).HasColumnName("create_by").HasMaxLength(25);
            entity.Property(x => x.CreateDate).HasColumnName("create_date").HasColumnType("datetime").HasDefaultValueSql("GETDATE()");
            entity.Property(x => x.UpdateBy).HasColumnName("update_by").HasMaxLength(25);
            entity.Property(x => x.UpdateDate).HasColumnName("update_date").HasColumnType("datetime");
        });

        modelBuilder.Entity<LimsQaqcCoaParameterTestEntity>(entity =>
        {
            entity.ToTable("t_lims_qaqc_coa_parameter_test");
            entity.HasKey(x => x.CoaParameterTestId).HasName("PK_t_lims_qaqc_coa_parameter_test");

            entity.Property(x => x.CoaParameterTestId).HasColumnName("coa_parameter_test_id").ValueGeneratedNever();
            entity.Property(x => x.ReceiptDetailId).HasColumnName("receipt_detail_id").IsRequired();
            entity.Property(x => x.ManageCoaParameterId).HasColumnName("manage_coa_parameter_id").HasDefaultValueSql("NEWID()");
            entity.Property(x => x.Result).HasColumnName("result").HasMaxLength(50).IsRequired();
            entity.Property(x => x.ResultBetween).HasColumnName("result_between").HasMaxLength(50);
            entity.Property(x => x.Remark).HasColumnName("remark").HasMaxLength(500);
            entity.Property(x => x.CreateBy).HasColumnName("create_by").HasMaxLength(25).IsRequired();
            entity.Property(x => x.CreateDate).HasColumnName("create_date").HasColumnType("datetime").HasDefaultValueSql("GETDATE()");
            entity.Property(x => x.UpdateBy).HasColumnName("update_by").HasMaxLength(25);
            entity.Property(x => x.UpdateDate).HasColumnName("update_date").HasColumnType("datetime");
            entity.Property(x => x.StandardResult).HasColumnName("standard_result").HasMaxLength(50);
            entity.Property(x => x.StandardResultBetween).HasColumnName("standard_result_between").HasMaxLength(50);
            entity.Property(x => x.PrefixLimit).HasColumnName("prefix_limit").HasMaxLength(50);
            entity.Property(x => x.FrequencyType).HasColumnName("frequency_type").HasMaxLength(100);
            entity.Property(x => x.TestUnit).HasColumnName("test_unit").HasMaxLength(50);
            entity.Property(x => x.RowVersion).HasColumnName("rowversion").IsRowVersion();
            entity.Property(x => x.Submit).HasColumnName("submit").HasColumnType("bit");
            entity.Property(x => x.SubmitStatus).HasColumnName("submit_status").HasMaxLength(25);
            entity.Property(x => x.SubmitBy).HasColumnName("submit_by").HasMaxLength(50);
            entity.Property(x => x.SubmitDate).HasColumnName("submit_date").HasColumnType("datetime");
            entity.Property(x => x.SubmitRemark).HasColumnName("submit_remark").HasColumnType("varchar(500)");
            entity.Property(x => x.Approve).HasColumnName("approve").HasColumnType("bit");
            entity.Property(x => x.ApproveStatus).HasColumnName("approve_status").HasMaxLength(25);
            entity.Property(x => x.ApproveBy).HasColumnName("approve_by").HasMaxLength(50);
            entity.Property(x => x.ApproveDate).HasColumnName("approve_date").HasColumnType("datetime");
            entity.Property(x => x.ApproveRemark).HasColumnName("approve_remark").HasColumnType("varchar(500)");

            entity.HasOne(x => x.InboundReceive)
                .WithMany(x => x.CoaParameterTests)
                .HasForeignKey(x => x.ReceiptDetailId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_t_lims_qaqc_coa_parameter_test_t_lims_inbound_receive_bulk");
        });

        modelBuilder.Entity<LimsQaqcCoaParameterTransactionEntity>(entity =>
        {
            entity.ToTable("t_lims_qaqc_coa_parameter_transaction");
            entity.HasKey(x => x.TranId).HasName("PK_t_lims_qaqc_coa_parameter_log");

            entity.Property(x => x.TranId).HasColumnName("tran_id").HasDefaultValueSql("NEWID()");
            entity.Property(x => x.LogType).HasColumnName("log_type").HasMaxLength(25);
            entity.Property(x => x.ReceiptDetailId).HasColumnName("receipt_detail_id").IsRequired();
            entity.Property(x => x.ManageCoaParameterId).HasColumnName("manage_coa_parameter_id").IsRequired();
            entity.Property(x => x.Result).HasColumnName("result").HasMaxLength(50).IsRequired();
            entity.Property(x => x.ResultBetween).HasColumnName("result_between").HasMaxLength(50);
            entity.Property(x => x.ReceiveDate).HasColumnName("receive_date").HasColumnType("datetime");
            entity.Property(x => x.UomId).HasColumnName("uom_id");
            entity.Property(x => x.TransationBy).HasColumnName("transation_by").HasMaxLength(50);
            entity.Property(x => x.TransactionDate).HasColumnName("transaction_date").HasColumnType("datetime");
            entity.Property(x => x.RowVersion).HasColumnName("rowversion").IsRowVersion().IsRequired();
        });
    }
}
