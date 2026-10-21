using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using NimbusLedger.Sccm.Abstractions;
using NimbusLedger.Sccm.Models;
using NimbusLedger.Sccm.Options;
using NimbusLedger.Sccm.Services;

namespace NimbusLedger.Tests;

public sealed class ConfigMgrCleanupTests
{
    [Theory]
    [InlineData("valid", 1)]
    [InlineData("missing-guid", 0)]
    [InlineData("name-only", 0)]
    [InlineData("fresh", 0)]
    [InlineData("unknown-activity", 0)]
    [InlineData("active-ad", 0)]
    [InlineData("cloud-only", 0)]
    [InlineData("zero-ad", 0)]
    [InlineData("zero-configmgr", 0)]
    public void RequiresStableAdIdentityAndOldActivityInEverySource(string scenario, int count)
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        snapshot = scenario switch
        {
            "missing-guid" => snapshot with { ConfigMgrDevices = [snapshot.ConfigMgrDevices![0] with { ActiveDirectoryObjectGuid = null }] },
            "name-only" => snapshot with { ConfigMgrDevices = [snapshot.ConfigMgrDevices![0] with { ActiveDirectoryObjectGuid = Guid.NewGuid() }] },
            "fresh" => snapshot with { ConfigMgrDevices = [snapshot.ConfigMgrDevices![0] with { LastActiveTime = DateTimeOffset.UtcNow }] },
            "unknown-activity" => snapshot with { ConfigMgrDevices = [snapshot.ConfigMgrDevices![0] with { LastActiveTime = null }] },
            "active-ad" => snapshot with { AllActiveDirectoryComputers = [snapshot.AllActiveDirectoryComputers[0] with { LastLogonTimestamp = DateTimeOffset.UtcNow }] },
            "cloud-only" => snapshot with { EntraDevices = [snapshot.EntraDevices[0] with { TrustType = "AzureAd" }] },
            "zero-ad" => snapshot with { AllActiveDirectoryComputers = [snapshot.AllActiveDirectoryComputers[0] with { LastLogonTimestamp = DateTimeOffset.FromFileTime(0) }] },
            "zero-configmgr" => snapshot with { ConfigMgrDevices = [snapshot.ConfigMgrDevices![0] with { LastActiveTime = DateTimeOffset.MinValue }] },
            _ => snapshot
        };
        (List<ConfigMgrDevice> actions, List<string> blockers) = Create(new(), new(), TestInventory.Options()).CreatePlan(snapshot);
        Assert.Empty(blockers);
        Assert.Equal(count, actions.Count);
    }

    [Fact]
    public void DuplicateGuidBlocksWholePlan()
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        snapshot = snapshot with
        {
            ConfigMgrDevices = [snapshot.ConfigMgrDevices![0], snapshot.ConfigMgrDevices[0] with { ResourceId = 2 }]
        };
        Assert.NotEmpty(Create(new(), new(), TestInventory.Options()).CreatePlan(snapshot).Blockers);
    }

    [Fact]
    public async Task DryRunNeverDeletesConfigMgrResources()
    {
        Client client = new();
        await Create(client, new(), TestInventory.Options()).PerformCleanupAsync(TestInventory.Snapshot(), default);
        Assert.Empty(client.Deleted);
    }

    [Fact]
    public async Task ExecutionAuditsAndDeletesExplicitResourceAfterRevalidation()
    {
        Client client = new();
        HybridLedgerOptions options = TestInventory.Options();
        options.Cleanup.DryRun = false;
        MemoryAuditLog audit = new();
        SccmCleanupService service = Create(client, audit, options, dryRun: false);
        await service.PerformCleanupAsync(TestInventory.Snapshot(), default);
        Assert.Equal([1], client.Deleted);
        Assert.Equal(["Started", "Completed"], audit.Outcomes);
    }

    [Fact]
    public async Task AuditWriteFailurePreventsDeletion()
    {
        Client client = new();
        HybridLedgerOptions options = TestInventory.Options();
        options.Cleanup.DryRun = false;
        SccmCleanupService service = Create(client, new() { Fail = true }, options, dryRun: false);
        await Assert.ThrowsAsync<IOException>(() => service.PerformCleanupAsync(TestInventory.Snapshot(), default));
        Assert.Empty(client.Deleted);
    }

    [Fact]
    public void InactiveThresholdIsAppliedToActivityTimestamp()
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        snapshot = snapshot with
        {
            ConfigMgrDevices = [snapshot.ConfigMgrDevices![0] with
            {
                IsObsolete = false, LastActiveTime = DateTimeOffset.UtcNow.AddDays(-20), ClientActiveStatus = 0
            }]
        };
        Assert.Empty(Create(new(), new(), TestInventory.Options()).CreatePlan(snapshot).Actions);
    }

    private static SccmCleanupService Create(Client client, MemoryAuditLog audit, HybridLedgerOptions ledger, bool dryRun = true) =>
        new(client, new FakeInventories(),
            Options.Create(new SccmCleanupOptions { Enabled = true, CleanupEnabled = true, AllowedResourceIds = [1], DryRun = dryRun }),
            Options.Create(ledger), NullLogger<SccmCleanupService>.Instance, audit);

    private sealed class Client : ISccmAdminServiceClient
    {
        internal List<int> Deleted { get; } = [];
        public async IAsyncEnumerable<CmDevice> GetDevicesAsync(string? odataFilter = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<bool> DeleteDeviceByResourceIdAsync(int resourceId, CancellationToken ct = default)
        {
            Deleted.Add(resourceId);
            return Task.FromResult(true);
        }
    }
}
