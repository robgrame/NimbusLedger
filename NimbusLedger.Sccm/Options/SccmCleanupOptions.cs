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
}
