namespace NimbusLedger.Core.Options;

/// <summary>
/// Top-level configuration envelope for the hybrid device ledger.
/// </summary>
public sealed class HybridLedgerOptions
{
    public ActiveDirectoryOptions ActiveDirectory { get; set; } = new();

    public GraphOptions Graph { get; set; } = new();

    public SnapshotOptions Snapshot { get; set; } = new();

    public SchedulerOptions Scheduler { get; set; } = new();

    public CleanupOptions Cleanup { get; set; } = new();
}
