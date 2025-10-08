namespace NimbusLedger.Core.Options;

/// <summary>
/// Controls the cadence of the background reconciliation worker.
/// </summary>
public sealed class SchedulerOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(60);

    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(10);
}
