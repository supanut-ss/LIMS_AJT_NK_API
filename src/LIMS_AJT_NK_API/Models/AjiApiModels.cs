using System.Text.Json;
using System.Text.Json.Serialization;

namespace LIMS_AJT_NK_API.Models;

public class AjiGetResultOcrRequest
{
    [JsonPropertyName("job_task_id")]
    public string JobTaskId { get; set; } = string.Empty;
}

public class AjiFeedbackRequest
{
    [JsonPropertyName("job_task_id")]
    public string JobTaskId { get; set; } = string.Empty;

    [JsonPropertyName("ocr_result")]
    public JsonElement OcrResult { get; set; }
}

public sealed record AjiApiCallResult(
    int StatusCode,
    string ContentType,
    string ResponseBody);
