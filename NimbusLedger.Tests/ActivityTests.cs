using NimbusLedger.Core.Models;
using NimbusLedger.Infrastructure.ActiveDirectory;

namespace NimbusLedger.Tests;

public sealed class ActivityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-timestamp")]
    public void UnknownLdapFiletimeDoesNotBecomeKnownOldActivity(string? value) =>
        Assert.Null(LdapActiveDirectoryClient.ParseLastLogonTimestamp(value));

    [Fact]
    public void ValidLdapFiletimeIsPreserved()
    {
        DateTimeOffset value = TestInventory.Now.AddDays(-120);
        Assert.Equal(value, LdapActiveDirectoryClient.ParseLastLogonTimestamp(
            value.ToFileTime().ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void LegacyZeroFiletimeAndEpochSentinelsAreNotActivityEvidence()
    {
        Assert.False(DeviceActivity.IsKnownOlderThan(DateTimeOffset.FromFileTime(0), TestInventory.Now));
        Assert.False(DeviceActivity.IsKnownOlderThan(DateTimeOffset.UnixEpoch, TestInventory.Now));
        Assert.False(DeviceActivity.IsKnownOlderThan(DateTimeOffset.MinValue, TestInventory.Now));
        Assert.True(DeviceActivity.IsKnownOlderThan(TestInventory.Now.AddDays(-120), TestInventory.Now));
    }
}
