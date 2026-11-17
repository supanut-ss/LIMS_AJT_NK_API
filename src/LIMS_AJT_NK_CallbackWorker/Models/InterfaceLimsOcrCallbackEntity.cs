namespace LIMS_AJT_NK_CallbackWorker.Models;

// Read-only subset of t_interface_lims_ocr_callback: just enough for the Worker
// to find not_matched callbacks worth retrying through IOcrRemapService. The
// matching columns themselves stay on the API side; the Worker only reads the
// callback_id and status here.
public class InterfaceLimsOcrCallbackEntity
{
    public Guid CallbackId { get; set; }
    public string JobTaskId { get; set; } = string.Empty;
    public string? InterfaceStatus { get; set; }
    public bool IsInterface { get; set; }
    public DateTime CreateDate { get; set; }
}
