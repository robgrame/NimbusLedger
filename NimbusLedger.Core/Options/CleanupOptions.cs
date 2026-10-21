namespace NimbusLedger.Core.Options;

public sealed class CleanupOptions
{
    public bool Enabled { get; set; } = false;
    public bool DeleteEntra { get; set; } = false;
    public bool DeleteIntune { get; set; } = false;
    public bool DryRun { get; set; } = true;
    public int IntuneFreshWindowDays { get; set; } = 30;
    public Guid[] AllowedAzureAdDeviceIds { get; set; } = [];
    public Guid[] ExcludedAzureAdDeviceIds { get; set; } = [];
    public string[] ExcludedDeviceNames { get; set; } = [];
    public int MaxDeletesPerRun { get; set; } = 5;
    public double MaxDeletePercentage { get; set; } = 5;
    public int MinimumAdDevices { get; set; } = 1;
    public int MinimumEntraDevices { get; set; } = 1;
    public int MinimumIntuneDevices { get; set; } = 1;
    public TimeSpan MaxSnapshotAge { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan MaxCollectionDuration { get; set; } = TimeSpan.FromMinutes(15);
}
