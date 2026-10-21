using NimbusLedger.Core.Models;

namespace NimbusLedger.Tests;

public sealed class DeviceIdentityTests
{
    [Theory]
    [InlineData("DEVICE01$", "device01")]
    [InlineData("device01.example.com", "device01")]
    [InlineData(" device01$ ", "device01")]
    public void NormalizesComputerNames(string input, string expected) =>
        Assert.Equal(expected, DeviceIdentityIndex.NormalizeName(input), ignoreCase: true);

    [Fact]
    public void CorrelatesAdObjectGuidWithCloudDeviceId()
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        DeviceIdentityIndex index = new(snapshot.EntraDevices.Select(DeviceIdentity.From));
        Assert.Equal(DeviceMatchKind.StableId, index.Match(DeviceIdentity.From(snapshot.AllActiveDirectoryComputers[0])));
    }

    [Fact]
    public void NameFallbackDoesNotGrantStableIdentity()
    {
        DeviceIdentityIndex index = new([new("one", [], ["DEVICE01$"])]);
        Assert.Equal(DeviceMatchKind.NameOnly, index.Match(new("query", [Guid.NewGuid()], ["device01.example.com"])));
    }

    [Fact]
    public void DuplicateNamesAreAmbiguous()
    {
        DeviceIdentityIndex index = new([new("one", [], ["device01.a.example.com"]), new("two", [], ["device01.b.example.com"])]);
        Assert.Equal(DeviceMatchKind.Ambiguous, index.Match(new("query", [], ["DEVICE01$"])));
    }

    [Fact]
    public void DuplicateStableIdsAreAmbiguous()
    {
        DeviceIdentityIndex index = new([new("one", [TestInventory.DeviceId], []), new("two", [TestInventory.DeviceId], [])]);
        Assert.Equal(DeviceMatchKind.Ambiguous, index.Match(new("query", [TestInventory.DeviceId], [])));
    }

    [Fact]
    public void StableIdentityTakesPrecedenceOverReusedName()
    {
        DeviceIdentityIndex index = new([new("one", [TestInventory.DeviceId], ["old-name"]), new("two", [Guid.NewGuid()], ["new-name"])]);
        Assert.Equal(DeviceMatchKind.StableId, index.Match(new("query", [TestInventory.DeviceId], ["new-name"])));
    }

    [Fact]
    public void ConfigMgrUsesDiscoveredAdGuid()
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        DeviceIdentityIndex index = new(snapshot.ConfigMgrDevices!.Select(DeviceIdentity.From));
        Assert.Equal(DeviceMatchKind.StableId, index.Match(DeviceIdentity.From(snapshot.AllActiveDirectoryComputers[0])));
    }
}
