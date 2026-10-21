using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NimbusLedger.Core.Options;
using NimbusLedger.Infrastructure.Storage;
using NimbusLedger.Web;

namespace NimbusLedger.Tests;

public sealed class SnapshotTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task IndependentReadersNeverSeePartiallyPublishedSnapshots(int iteration)
    {
        string directory = Path.Combine(Path.GetTempPath(), "NimbusLedgerTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            IOptions<HybridLedgerOptions> options = Options.Create(new HybridLedgerOptions { Snapshot = new() { RootPath = directory, HistorySize = 100 } });
            using FileLedgerSnapshotStore writer = new(options, NullLogger<FileLedgerSnapshotStore>.Instance);
            using FileLedgerSnapshotStore reader = new(options, NullLogger<FileLedgerSnapshotStore>.Instance);
            await writer.SaveSnapshotAsync(TestInventory.Snapshot(), default);
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    for (int index = 0; index < 40 + iteration; index++)
                        await writer.SaveSnapshotAsync(TestInventory.Snapshot(), default);
                }),
                Task.Run(async () =>
                {
                    for (int index = 0; index < 100; index++)
                        Assert.True((await reader.GetLatestSnapshotAsync(default))!.InventoryComplete);
                }));
            Assert.Empty(Directory.GetFiles(directory, "tmp-*.json"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("current", HealthStatus.Healthy)]
    [InlineData("missing", HealthStatus.Unhealthy)]
    [InlineData("stale", HealthStatus.Unhealthy)]
    [InlineData("legacy", HealthStatus.Unhealthy)]
    [InlineData("future", HealthStatus.Unhealthy)]
    public async Task ReadinessRequiresCompleteCurrentSnapshot(string scenario, HealthStatus expected)
    {
        MemorySnapshotStore store = new() { Snapshot = TestInventory.Snapshot() };
        store.Snapshot = scenario switch
        {
            "missing" => null,
            "stale" => store.Snapshot with { CollectionStartedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            "legacy" => store.Snapshot with { InventoryComplete = false },
            "future" => store.Snapshot with { CapturedAt = DateTimeOffset.UtcNow.AddDays(1) },
            _ => store.Snapshot
        };
        SnapshotHealthCheck check = new(store, Options.Create(new HybridLedgerOptions()));
        Assert.Equal(expected, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }
}
