using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LIMS_AJT_NK_API.Models;

public class OcrCallbackRequest
{
    [Required]
    [JsonPropertyName("job_task_id")]
    public string JobTaskId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("ocr_result")]
    public List<OcrResultRequest> OcrResult { get; set; } = [];

    [JsonPropertyName("summary")]
    public OcrCallbackSourceSummaryRequest? Summary { get; set; }
}

public class OcrCallbackSourceSummaryRequest
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("success")]
    public int Success { get; set; }

    [JsonPropertyName("processing")]
    public int Processing { get; set; }

    [JsonPropertyName("fail")]
    public int Fail { get; set; }

    [JsonPropertyName("other")]
    public int Other { get; set; }
}

public class OcrResultRequest
{
    [JsonPropertyName("page_id")]
    public int? PageId { get; set; }

    [JsonPropertyName("file_id")]
    public string? FileId { get; set; }

    [JsonPropertyName("tracking_id")]
    public string? TrackingId { get; set; }

    [JsonPropertyName("tracking_status")]
    public string? TrackingStatus { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("body_json")]
    public OcrBodyJsonRequest? BodyJson { get; set; }

    [JsonPropertyName("confident")]
    public JsonElement? Confident { get; set; }
}

public class OcrBodyJsonRequest
{
    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("document_type")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("document_classification")]
    public string? DocumentClassification { get; set; }

    [JsonPropertyName("Supplier_name")]
    public string? SupplierName { get; set; }

    [JsonPropertyName("lot_number")]
    public string? LotNumber { get; set; }

    [JsonPropertyName("origin_supplier_name")]
    public string? OriginSupplierName { get; set; }

    [JsonPropertyName("origin_product_name")]
    public string? OriginProductName { get; set; }

    [JsonPropertyName("expiry_date")]
    public string? ExpiryDate { get; set; }

    [JsonPropertyName("mfg_date")]
    public string? MfgDate { get; set; }

    [JsonPropertyName("Internal_lot")]
    public string? InternalLot { get; set; }

    [JsonPropertyName("quantity")]
    public string? Quantity { get; set; }

    [JsonPropertyName("body_item")]
    public List<OcrBodyItemRequest> BodyItem { get; set; } = [];
}

public class OcrBodyItemRequest
{
    [JsonPropertyName("parameter_name")]
    public string? ParameterName { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("uom")]
    public string? Uom { get; set; }
}

public class InterfaceLimsOcrCallbackEntity
{
    public Guid CallbackId { get; set; }
    public string JobTaskId { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public string? InterfaceStatus { get; set; }
    public string? InterfaceSummaryJson { get; set; }
    public string? SourceSummaryJson { get; set; }
    public bool IsInterface { get; set; }
    public string? CreateBy { get; set; }
    public DateTime CreateDate { get; set; }

    public ICollection<InterfaceLimsOcrResultEntity> Results { get; set; } = new List<InterfaceLimsOcrResultEntity>();
}

public class InterfaceLimsOcrResultEntity
{
    public Guid ResultId { get; set; }
    public Guid CallbackId { get; set; }
    public InterfaceLimsOcrCallbackEntity? Callback { get; set; }
    public bool IsInterface { get; set; }

    public int PageId { get; set; }
    public string? FileId { get; set; }
    public string? TrackingId { get; set; }
    public string TrackingStatus { get; set; } = string.Empty;
    public string? Status { get; set; }

    public string? ProductName { get; set; }
    public string? DocumentType { get; set; }
    public string? DocumentClassification { get; set; }
    public string? SupplierName { get; set; }
    public string? LotNumber { get; set; }
    public string? OriginSupplierName { get; set; }
    public string? OriginProductName { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? MfgDate { get; set; }
    public string? InternalLot { get; set; }
    public decimal? Quantity { get; set; }
    public string? QuantityUom { get; set; }
    public string? ConfidenceJson { get; set; }

    public string? CreateBy { get; set; }
    public DateTime CreateDate { get; set; }

    public ICollection<InterfaceLimsOcrResultItemEntity> Items { get; set; } = new List<InterfaceLimsOcrResultItemEntity>();
}

public class InterfaceLimsOcrResultItemEntity
{
    public Guid ItemId { get; set; }
    public Guid ResultId { get; set; }
    public InterfaceLimsOcrResultEntity? OcrResult { get; set; }
    public bool IsInterface { get; set; }
    public int Seq { get; set; }

    public string ParameterName { get; set; } = string.Empty;
    public string? ResultValue { get; set; }
    public string? Uom { get; set; }

    public string? CreateBy { get; set; }
    public DateTime CreateDate { get; set; }
}
