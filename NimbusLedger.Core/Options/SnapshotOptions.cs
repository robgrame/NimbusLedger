namespace NimbusLedger.Core.Options;

/// <summary>
/// Configures how snapshots are persisted and exposed to the web dashboard.
/// </summary>
public sealed class SnapshotOptions
{
    /// <summary>
    /// Directory where snapshot files are stored. Defaults to ./data.
    /// </summary>
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>
    /// File name used for the most recent snapshot.
    /// </summary>
    public string LatestFileName { get; set; } = "latest-snapshot.json";

    /// <summary>
    /// Maximum number of historical snapshots to retain.
    /// </summary>
    public int HistorySize { get; set; } = 10;
}
