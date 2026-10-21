namespace NimbusLedger.Sccm.Options;

public sealed class SccmCleanupOptions
{
    public bool Enabled { get; set; } = false;
    public string? AdminServiceBaseUrl { get; set; }
    public string? DomainUsername { get; set; }
    public string? DomainPassword { get; set; }
    public string? Domain { get; set; }
    public int InactiveDaysThreshold { get; set; } = 90;
    public int ObsoleteDaysThreshold { get; set; } = 7;
    public bool CleanupEnabled { get; set; } = false;
    public bool DryRun { get; set; } = true;
    public int[] AllowedResourceIds { get; set; } = [];
    public int[] ExcludedResourceIds { get; set; } = [];
    public int MaxDeletesPerRun { get; set; } = 5;
}
