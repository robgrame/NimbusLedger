using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;

namespace NimbusLedger.Tests;

internal static class TestInventory
{
    internal static readonly Guid DeviceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    internal static HybridLedgerOptions Options() => new()
    {
        Cleanup = new()
        {
            Enabled = true, DeleteEntra = true, DeleteIntune = true,
            AllowedAzureAdDeviceIds = [DeviceId], MaxDeletePercentage = 100
        }
    };

    internal static LedgerSnapshot Snapshot()
    {
        DateTimeOffset stale = Now.AddDays(-120);
        ComputerAccount ad = new(DeviceId, "DEVICE01$", "CN=DEVICE01,DC=example,DC=com",
            "device01.example.com", "Windows", "10", stale, stale, null);
        DeviceRecord entra = new(Guid.Parse("20000000-0000-0000-0000-000000000001"),
            "DEVICE01", DeviceId, "Windows", "10", stale, "EntraID") { TrustType = "ServerAd" };
        DeviceRecord intune = new(Guid.Parse("30000000-0000-0000-0000-000000000001"),
            "DEVICE01", DeviceId, "Windows", "10", stale, "Intune");
        return new(Now, [], [entra], [intune], [], [], new(0, 1, 1, 0, 0, 1))
        {
            CollectionStartedAt = Now.AddSeconds(-1), InventoryComplete = true,
            AllActiveDirectoryComputers = [ad],
            ConfigMgrDevices = [new(1, "DEVICE01", stale, true, 0) { ActiveDirectoryObjectGuid = DeviceId }]
        };
    }

    internal sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
