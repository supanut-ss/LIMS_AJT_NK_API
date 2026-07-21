using System.Text.Json.Serialization;

namespace LIMS_AJT_NK_CallbackWorker.Models;

public class CallbackRequest
{
    [JsonPropertyName("job_task_id")]
    public string JobTaskId { get; set; } = string.Empty;

    [JsonPropertyName("ocr_result")]
    public List<OcrResult> OcrResult { get; set; } = [];
}

public class OcrResult
{
    [JsonPropertyName("page_id")]
    public int PageId { get; set; }

    [JsonPropertyName("tracking_id")]
    public string? TrackingId { get; set; }

    [JsonPropertyName("tracking_status")]
    public string? TrackingStatus { get; set; }

    [JsonPropertyName("body_json")]
    public OcrBodyJson BodyJson { get; set; } = new();
}

public class OcrBodyJson
{
    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("document_type")]
    public string? DocumentType { get; set; }

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
    public List<OcrBodyItem> BodyItem { get; set; } = [];
}

public class OcrBodyItem
{
    [JsonPropertyName("parameter_name")]
    public string? ParameterName { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("uom")]
    public string? Uom { get; set; }
}