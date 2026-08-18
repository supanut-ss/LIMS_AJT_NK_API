namespace LIMS_AJT_NK_API.Models;

public class InterfaceLimsOcrLogEntity
{
    public Guid LogId { get; set; }
    public string ApiName { get; set; } = string.Empty;
    public string? RequestUrl { get; set; }
    public string? FlowId { get; set; }
    public string? JobTaskId { get; set; }
    public string? FilePath { get; set; }
    public string? RequestPayload { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponsePayload { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SourceSystem { get; set; }
    public string WorkStatus { get; set; } = "submitted";
    public int AttemptCount { get; set; }
    public string? FinalPath { get; set; }
    public DateTime? CompletedDate { get; set; }
    public bool IsInterface { get; set; }
    public string? CreateBy { get; set; }
    public DateTime CreateDate { get; set; }
}
