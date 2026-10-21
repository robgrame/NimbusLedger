using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;

namespace NimbusLedger.Tests;

internal sealed class FakeInventories : IActiveDirectoryClient, IEntraIdDeviceClient, IIntuneDeviceClient, IConfigMgrDeviceClient, ILedgerService
{
    internal LedgerSnapshot Snapshot { get; set; } = TestInventory.Snapshot();
    internal bool FailIntune { get; set; }
    internal bool FailDelete { get; set; }
    internal List<string> Deletions { get; } = [];
    internal int AdReads { get; private set; }
    public bool Enabled { get; set; } = true;

    public Task<IReadOnlyList<ComputerAccount>> GetComputersAsync(CancellationToken cancellationToken)
    {
        AdReads++;
        return Task.FromResult(Snapshot.AllActiveDirectoryComputers);
    }

    Task<IReadOnlyList<DeviceRecord>> IEntraIdDeviceClient.GetDevicesAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot.EntraDevices);
    Task<IReadOnlyList<ConfigMgrDevice>> IConfigMgrDeviceClient.GetDevicesAsync(CancellationToken cancellationToken) =>
        Enabled ? Task.FromResult(Snapshot.ConfigMgrDevices ?? []) : throw new InvalidOperationException("Disabled inventory was called.");

    public Task<IReadOnlyList<DeviceRecord>> GetManagedDevicesAsync(CancellationToken cancellationToken) =>
        FailIntune ? throw new InvalidDataException("Simulated incomplete inventory.") : Task.FromResult(Snapshot.IntuneDevices);

    public Task DeleteDeviceAsync(Guid id, CancellationToken cancellationToken)
    {
        Deletions.Add("EntraID");
        return Task.CompletedTask;
    }

    public Task DeleteManagedDeviceAsync(Guid id, CancellationToken cancellationToken)
    {
        Deletions.Add("Intune");
        if (FailDelete) throw new HttpRequestException("Simulated lost deletion response.");
        return Task.CompletedTask;
    }

    public Task<LedgerSnapshot> ReconcileAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);
}

internal sealed class MemorySnapshotStore : ILedgerSnapshotStore
{
    internal LedgerSnapshot? Snapshot { get; set; }
    public Task SaveSnapshotAsync(LedgerSnapshot snapshot, CancellationToken cancellationToken)
    {
        Snapshot = snapshot;
        return Task.CompletedTask;
    }

    public Task<LedgerSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);
}

internal sealed class MemoryAuditLog : ICleanupAuditLog
{
    internal List<string> Outcomes { get; } = [];
    internal bool Fail { get; set; }
    public Task WriteAsync(string source, string objectId, string outcome, CancellationToken cancellationToken)
    {
        if (Fail) throw new IOException("Simulated audit storage failure.");
        Outcomes.Add(outcome);
        return Task.CompletedTask;
    }
}
