using System.Text.Json.Serialization;

namespace LIMS_AJT_NK_API.Services;

public static class OcrInterfaceStatuses
{
    public const string Completed = "completed";
    public const string Partial = "partial";
    public const string NotMatched = "not_matched";
}

public sealed class OcrCallbackInterfaceSummary
{
    [JsonPropertyName("interface_status")]
    public string InterfaceStatus { get; set; } = OcrInterfaceStatuses.NotMatched;

    [JsonPropertyName("updated_item_count")]
    public int UpdatedItemCount { get; set; }

    [JsonPropertyName("skipped_item_count")]
    public int SkippedItemCount { get; set; }

    [JsonPropertyName("pages")]
    public List<OcrCallbackInterfacePageResult> Pages { get; set; } = [];

    [JsonIgnore]
    public bool IsCompleted => InterfaceStatus == OcrInterfaceStatuses.Completed;
}

public sealed class OcrCallbackInterfacePageResult
{
    [JsonPropertyName("page_id")]
    public int PageId { get; set; }

    [JsonPropertyName("receipt_detail_id")]
    public Guid? ReceiptDetailId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }

    [JsonPropertyName("updated_item_count")]
    public int UpdatedItemCount { get; set; }

    [JsonPropertyName("skipped_item_count")]
    public int SkippedItemCount { get; set; }

    [JsonPropertyName("items")]
    public List<OcrCallbackInterfaceItemResult> Items { get; set; } = [];
}

public sealed class OcrCallbackInterfaceItemResult
{
    [JsonPropertyName("seq")]
    public int Seq { get; set; }

    [JsonPropertyName("parameter_name")]
    public string ParameterName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}
