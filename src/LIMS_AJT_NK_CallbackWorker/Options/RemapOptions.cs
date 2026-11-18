namespace LIMS_AJT_NK_CallbackWorker.Options;

// Automatic remap of callbacks that are still not_matched / partial. Set
// "Remap": { "Enabled": false } in appsettings.json to turn it off.
public class RemapOptions
{
    public const string SectionName = "Remap";

    public bool Enabled { get; set; } = true;

    // Minimum seconds between two remap passes.
    public int IntervalSeconds { get; set; } = 300;
}
