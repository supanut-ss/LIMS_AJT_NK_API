using System.Text.Json.Serialization;

namespace LIMS_AJT_NK_CallbackWorker.Models;

public class UpdateMasterDataRequest
{
    [JsonPropertyName("flow_id")]
    public string FlowId { get; set; } = string.Empty;
}
