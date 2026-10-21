using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using System.Text.Json;

namespace NimbusLedger.Web;

public sealed class SnapshotHealthCheck(
    ILedgerSnapshotStore store,
    IOptions<HybridLedgerOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            LedgerSnapshot? snapshot = await store.GetLatestSnapshotAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return snapshot is not null && snapshot.InventoryComplete
                && snapshot.CollectionStartedAt != default && snapshot.CapturedAt >= snapshot.CollectionStartedAt
                && snapshot.CapturedAt <= now && now - snapshot.CollectionStartedAt <= options.Value.Snapshot.MaxAge
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("A complete, current inventory snapshot is not available.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return HealthCheckResult.Unhealthy("Snapshot storage is unavailable.", exception);
        }
    }
}
