namespace LIMS_AJT_NK_API.Models;

public class LimsCoaParameterMappingEntity
{
    public Guid CoaMappingParameterId { get; set; }
    public string? CoaParameterName { get; set; }
    public string? OcrParameterName { get; set; }
    public string IsActive { get; set; } = string.Empty;
    public string CreateBy { get; set; } = string.Empty;
    public DateTime CreateDate { get; set; }
    public string? UpdateBy { get; set; }
    public DateTime? UpdateDate { get; set; }
    public byte[]? RowVersion { get; set; }
}

public class WmsItemEntity
{
    public Guid ItemMasterId { get; set; }
    public string? Description { get; set; }
}

public class LimsCoaParameterEntity
{
    public Guid CoaParameterId { get; set; }
    public string? CoaParameterName { get; set; }
    public string IsActive { get; set; } = string.Empty;
}

public class LimsManageCoaParameterEntity
{
    public Guid ManageCoaParameterId { get; set; }
    public Guid ItemRmId { get; set; }
    public Guid CoaParameterId { get; set; }
    public string IsActive { get; set; } = string.Empty;
}

public class LimsInboundReceiveEntity
{
    public Guid ReceiptDetailId { get; set; }
    public Guid? WhMasterId { get; set; }
    public string? WhId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierCode { get; set; }
    public string? InboundOrderNumber { get; set; }
    public Guid? ItemMasterId { get; set; }
    public string? ItemNumber { get; set; }
    public double? QtyReceived { get; set; }
    public Guid? UomId { get; set; }
    public string? UomCode { get; set; }
    public string? LotNumber { get; set; }
    public string? ExpiryDate { get; set; }
    public DateTime? ReceiveDate { get; set; }
    public DateTime? MfgDate { get; set; }
    public string? ExternalLot { get; set; }
    public bool? Appearance { get; set; }
    public bool? Contaminant { get; set; }
    public bool? Metal { get; set; }
    public string? Remark { get; set; }
    public DateTime CreateDate { get; set; }
    public string CreateBy { get; set; } = string.Empty;
    public DateTime? UpdateDate { get; set; }
    public string? UpdateBy { get; set; }
    public string? ApproveStatus { get; set; }
    public string? ApproveBy { get; set; }
    public DateTime? ApproveDate { get; set; }
    public string? ApproveRemark { get; set; }
    public string? SupervisorSubmitStatus { get; set; }
    public string? SupervisorSubmitBy { get; set; }
    public DateTime? SupervisorSubmitDate { get; set; }
    public string? SupervisorSubmitRemark { get; set; }
    public string? QmrReviewStatus { get; set; }
    public string? QmrReviewBy { get; set; }
    public DateTime? QmrReviewDate { get; set; }
    public string? QmrReviewRemark { get; set; }
    public string? RefExLot { get; set; }
    public string? SupplierName { get; set; }
    public string? ItemRmCode { get; set; }
    public string? ItemRmName { get; set; }

    public ICollection<LimsQaqcCoaParameterTestEntity> CoaParameterTests { get; set; } = [];
}

public class LimsQaqcScanEntity
{
    public Guid QaqcScanId { get; set; }
    public Guid ReceiptDetailId { get; set; }
    public string? ItemNumber { get; set; }
    public string? ItemRmCode { get; set; }
    public string? ItemRmName { get; set; }
    public string? LotNumber { get; set; }
    public string? ExpiryDate { get; set; }
    public DateTime? MfgDate { get; set; }
    public string? LimsScaned { get; set; }
    public string? QcCenterStatus { get; set; }
    public string? QcCenterScanBy { get; set; }
    public DateTime? QcCenterScanDate { get; set; }
    public string? QcCoaStatus { get; set; }
    public string? QcCoaBy { get; set; }
    public DateTime? QcCoaDate { get; set; }
    public string? QcCoaRemark { get; set; }
    public string? QaCoaStatus { get; set; }
    public string? QaCoaBy { get; set; }
    public DateTime? QaCoaDate { get; set; }
    public string? QaCoaRemark { get; set; }
    public string? QcSupervisorStatus { get; set; }
    public string? QcSupervisorBy { get; set; }
    public DateTime? QcSupervisorDate { get; set; }
    public string? QcSupervisorRemark { get; set; }
    public string? QaSupervisorStatus { get; set; }
    public string? QaSupervisorBy { get; set; }
    public DateTime? QaSupervisorDate { get; set; }
    public string? QaSupervisorRemark { get; set; }
    public int? MicrobioPlateNo { get; set; }
    public bool? IsHold { get; set; }
    public bool? IsUnhold { get; set; }
}

public class LimsDocumentEntity
{
    public int DocumentId { get; set; }
    public Guid PkId { get; set; }
    public string? DocumentGroup { get; set; }
    public string? DocumentType { get; set; }
    public string? DocumentCode { get; set; }
    public string? DocumentName { get; set; }
    public string? DocumentPath { get; set; }
    public string? Description { get; set; }
    public string? Udf1 { get; set; }
    public string? Udf2 { get; set; }
    public string? Udf3 { get; set; }
    public bool? IsActive { get; set; }
    public string? CreateBy { get; set; }
    public DateTime? CreateDate { get; set; }
    public string? UpdateBy { get; set; }
    public DateTime? UpdateDate { get; set; }
}

public class LimsQaqcCoaParameterTestEntity
{
    public Guid CoaParameterTestId { get; set; }
    public Guid ReceiptDetailId { get; set; }
    public Guid ManageCoaParameterId { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? ResultBetween { get; set; }
    public string? Remark { get; set; }
    public string CreateBy { get; set; } = string.Empty;
    public DateTime CreateDate { get; set; }
    public string? UpdateBy { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? StandardResult { get; set; }
    public string? StandardResultBetween { get; set; }
    public string? PrefixLimit { get; set; }
    public string? FrequencyType { get; set; }
    public string? TestUnit { get; set; }
    public byte[]? RowVersion { get; set; }
    public bool? Submit { get; set; }
    public string? SubmitStatus { get; set; }
    public string? SubmitBy { get; set; }
    public DateTime? SubmitDate { get; set; }
    public string? SubmitRemark { get; set; }
    public bool? Approve { get; set; }
    public string? ApproveStatus { get; set; }
    public string? ApproveBy { get; set; }
    public DateTime? ApproveDate { get; set; }
    public string? ApproveRemark { get; set; }

    public LimsInboundReceiveEntity? InboundReceive { get; set; }
}

public class LimsQaqcCoaParameterTransactionEntity
{
    public Guid TranId { get; set; }
    public string? LogType { get; set; }
    public Guid ReceiptDetailId { get; set; }
    public Guid ManageCoaParameterId { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? ResultBetween { get; set; }
    public DateTime? ReceiveDate { get; set; }
    public Guid? UomId { get; set; }
    public string? TransationBy { get; set; }
    public DateTime? TransactionDate { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
