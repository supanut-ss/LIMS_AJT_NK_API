namespace LIMS_AJT_NK_CallbackWorker.Options;

public class InputOcrApiOptions
{
    public const string SectionName = "InputOcrApi";

    public bool Enabled { get; set; } = true;
    public string EndpointUrl { get; set; } = "http://localhost:5117/input_ocr";
    public int IntervalSeconds { get; set; } = 30;

    public string? FlowId { get; set; }
    public string CallbackUrl { get; set; } = "http://localhost:5117/api/call_back";

    public string InboundDirectory { get; set; } = "1_Inbound";
    public string ProcessingDirectory { get; set; } = "2_Processing";
    public string SuccessDirectory { get; set; } = "3_Success";
    public string ErrorDirectory { get; set; } = "4_Error";
}