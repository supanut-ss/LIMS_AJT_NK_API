using System.Text.Json.Serialization;

namespace LIMS_AJT_NK_CallbackWorker.Models;

public class InputOcrRequest
{
    [JsonPropertyName("flow_id")]
    public string FlowId { get; set; } = string.Empty;

    [JsonPropertyName("job_task_id")]
    public string JobTaskId { get; set; } = string.Empty;

    [JsonPropertyName("s3_path_image")]
    public string S3PathImage { get; set; } = string.Empty;

    [JsonPropertyName("callback_url")]
    public string CallbackUrl { get; set; } = string.Empty;
}