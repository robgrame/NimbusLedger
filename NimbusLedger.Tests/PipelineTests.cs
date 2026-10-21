using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using NimbusLedger.Infrastructure.Services;

namespace NimbusLedger.Tests;

public sealed class PipelineTests
{
    [Fact]
    public async Task LostDeletionResponseIsAuditedAsUnknownAndStopsSubsequentActions()
    {
        FakeInventories sources = new() { FailDelete = true };
        HybridLedgerOptions settings = TestInventory.Options();
        settings.Cleanup.DryRun = false;
        IOptions<HybridLedgerOptions> options = Options.Create(settings);
        MemoryAuditLog audit = new();
        CleanupService service = new(sources, sources, sources, new CleanupPlanner(options, TimeProvider.System),
            options, NullLogger<CleanupService>.Instance, audit);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.PerformCleanupAsync(TestInventory.Snapshot(), default));
        Assert.Equal(["Intune"], sources.Deletions);
        Assert.Equal(["Started", "OutcomeUnknown"], audit.Outcomes);
    }

    [Fact]
    public async Task AuditFailurePreventsCloudDeletion()
    {
        FakeInventories sources = new();
        HybridLedgerOptions settings = TestInventory.Options();
        settings.Cleanup.DryRun = false;
        IOptions<HybridLedgerOptions> options = Options.Create(settings);
        CleanupService service = new(sources, sources, sources, new CleanupPlanner(options, TimeProvider.System),
            options, NullLogger<CleanupService>.Instance, new MemoryAuditLog { Fail = true });
        await Assert.ThrowsAsync<IOException>(() => service.PerformCleanupAsync(TestInventory.Snapshot(), default));
        Assert.Empty(sources.Deletions);
    }

    [Fact]
    public async Task ReconcilesAllFourSourcesAndPersistsCompletedSnapshot()
    {
        FakeInventories sources = new();
        sources.Snapshot = sources.Snapshot with
        {
            AllActiveDirectoryComputers = [sources.Snapshot.AllActiveDirectoryComputers[0] with { LastLogonTimestamp = TestInventory.Now }]
        };
        MemorySnapshotStore store = new();
        IOptions<HybridLedgerOptions> options = Options.Create(new HybridLedgerOptions());
        LedgerReconciliationService service = new(sources, sources, sources, store, options,
            NullLogger<LedgerReconciliationService>.Instance, sources, new CleanupPlanner(options, TimeProvider.System));
        LedgerSnapshot snapshot = await service.ReconcileAsync(default);
        Assert.Same(snapshot, store.Snapshot);
        Assert.True(snapshot.InventoryComplete);
        Assert.Single(snapshot.ConfigMgrDevices!);
        Assert.Empty(snapshot.MissingInConfigMgr!);
        Assert.Empty(snapshot.MissingInEntra);
        Assert.Empty(snapshot.MissingInIntune);
        Assert.Equal(0, snapshot.NameOnlyMatches);
    }

    [Fact]
    public async Task FailureDoesNotPublishPartialInventory()
    {
        FakeInventories sources = new() { FailIntune = true };
        MemorySnapshotStore store = new();
        IOptions<HybridLedgerOptions> options = Options.Create(new HybridLedgerOptions());
        LedgerReconciliationService service = new(sources, sources, sources, store, options,
            NullLogger<LedgerReconciliationService>.Instance, sources, new CleanupPlanner(options, TimeProvider.System));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReconcileAsync(default));
        Assert.Null(store.Snapshot);
    }

    [Fact]
    public async Task OptionalConfigMgrDoesNotPretendEmptyInventoryWasCollected()
    {
        FakeInventories sources = new() { Enabled = false };
        IOptions<HybridLedgerOptions> options = Options.Create(new HybridLedgerOptions());
        LedgerReconciliationService service = new(sources, sources, sources, new MemorySnapshotStore(), options,
            NullLogger<LedgerReconciliationService>.Instance, sources, new CleanupPlanner(options, TimeProvider.System));
        LedgerSnapshot snapshot = await service.ReconcileAsync(default);
        Assert.Null(snapshot.ConfigMgrDevices);
        Assert.Null(snapshot.MissingInConfigMgr);
    }

    [Fact]
    public async Task DuplicateObjectIdsFailBeforeSnapshotPublication()
    {
        FakeInventories sources = new();
        sources.Snapshot = sources.Snapshot with { IntuneDevices = [sources.Snapshot.IntuneDevices[0], sources.Snapshot.IntuneDevices[0]] };
        MemorySnapshotStore store = new();
        IOptions<HybridLedgerOptions> options = Options.Create(new HybridLedgerOptions());
        LedgerReconciliationService service = new(sources, sources, sources, store, options,
            NullLogger<LedgerReconciliationService>.Instance, sources, new CleanupPlanner(options, TimeProvider.System));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReconcileAsync(default));
        Assert.Null(store.Snapshot);
    }

    [Fact]
    public async Task DryRunNeverCallsDelete()
    {
        FakeInventories sources = new();
        IOptions<HybridLedgerOptions> options = Options.Create(TestInventory.Options());
        CleanupService service = new(sources, sources, sources, new CleanupPlanner(options, TimeProvider.System),
            options, NullLogger<CleanupService>.Instance, new MemoryAuditLog());
        await service.PerformCleanupAsync(TestInventory.Snapshot(), default);
        Assert.Empty(sources.Deletions);
        Assert.Equal(0, sources.AdReads);
    }

    [Fact]
    public async Task ExecutionRevalidatesInventoryAndDeletesIntuneBeforeEntra()
    {
        FakeInventories sources = new();
        HybridLedgerOptions settings = TestInventory.Options();
        settings.Cleanup.DryRun = false;
        IOptions<HybridLedgerOptions> options = Options.Create(settings);
        CleanupService service = new(sources, sources, sources, new CleanupPlanner(options, TimeProvider.System),
            options, NullLogger<CleanupService>.Instance, new MemoryAuditLog());
        await service.PerformCleanupAsync(TestInventory.Snapshot(), default);
        Assert.Equal(["Intune", "EntraID"], sources.Deletions);
        Assert.Equal(1, sources.AdReads);
    }

    [Fact]
    public async Task ActivityChangeDuringPreflightStopsAllDeletes()
    {
        FakeInventories sources = new();
        sources.Snapshot = sources.Snapshot with
        {
            IntuneDevices = [sources.Snapshot.IntuneDevices[0] with { LastSyncDateTime = DateTimeOffset.UtcNow }]
        };
        HybridLedgerOptions settings = TestInventory.Options();
        settings.Cleanup.DryRun = false;
        IOptions<HybridLedgerOptions> options = Options.Create(settings);
        CleanupService service = new(sources, sources, sources, new CleanupPlanner(options, TimeProvider.System),
            options, NullLogger<CleanupService>.Instance, new MemoryAuditLog());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PerformCleanupAsync(TestInventory.Snapshot(), default));
        Assert.Empty(sources.Deletions);
    }
}
