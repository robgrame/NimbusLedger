namespace NimbusLedger.Core.Models;

public static class DeviceActivity
{
    public static bool IsKnownOlderThan(DateTimeOffset? timestamp, DateTimeOffset cutoff) =>
        timestamp is DateTimeOffset value && value > DateTimeOffset.UnixEpoch && value < cutoff;
}
