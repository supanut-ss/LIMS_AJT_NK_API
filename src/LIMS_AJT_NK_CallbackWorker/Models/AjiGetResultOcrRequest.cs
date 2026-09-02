using System.Text.Json.Serialization;

namespace LIMS_AJT_NK_CallbackWorker.Models;

public class AjiGetResultOcrRequest
{
    [JsonPropertyName("job_task_id")]
    public string JobTaskId { get; set; } = string.Empty;
}
