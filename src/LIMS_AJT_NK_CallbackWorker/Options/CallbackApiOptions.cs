namespace LIMS_AJT_NK_CallbackWorker.Options;

public class CallbackApiOptions
{
    public const string SectionName = "CallbackApi";

    public bool Enabled { get; set; } = true;
    public string EndpointUrl { get; set; } = "http://192.168.1.135:5117/api/call_back";
    public int IntervalSeconds { get; set; } = 30;
    public string JobTaskId { get; set; } = "worker-job-task-001";
}