namespace LIMS_AJT_NK_API.Models;

public class InterfaceLimsOcrConfigApiEntity
{
    public Guid ConfigId { get; set; }
    public bool IsEnabled { get; set; }
    public string InputOcrUrl { get; set; } = string.Empty;
    public string? InputOcrFileUrl { get; set; }
    public string SubmissionMode { get; set; } = "path";
    public string InputOcrFileFieldName { get; set; } = "file";
    public string? CallbackUrl { get; set; }
    public string InboundDirectory { get; set; } = string.Empty;
    public string ProcessingDirectory { get; set; } = string.Empty;
    public string SuccessDirectory { get; set; } = string.Empty;
    public string ErrorDirectory { get; set; } = string.Empty;
    public string? DocumentHostDirectory { get; set; }
    public string? DocumentWebPath { get; set; }
    public string? DocumentGroup { get; set; }
    public string? FlowId { get; set; }
    public int IntervalSeconds { get; set; }
    public int FileStableSeconds { get; set; }
    public int MaxSendAttempts { get; set; }
    public int RetryDelaySeconds { get; set; }
    public int RequestTimeoutSeconds { get; set; }
    public bool IsInterface { get; set; }
    public string? CreateBy { get; set; }
    public DateTime CreateDate { get; set; }
}
