namespace NimbusLedger.Core.Options;

public sealed class CleanupOptions
{
    public bool Enabled { get; set; } = false;
    public bool DeleteEntra { get; set; } = false;
    public bool DeleteIntune { get; set; } = false;
    public bool DryRun { get; set; } = true;
    public int IntuneFreshWindowDays { get; set; } = 30;
}
