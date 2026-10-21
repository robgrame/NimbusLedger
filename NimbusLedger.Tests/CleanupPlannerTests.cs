using Microsoft.Extensions.Options;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using NimbusLedger.Infrastructure.Services;

namespace NimbusLedger.Tests;

public sealed class CleanupPlannerTests
{
    private static CleanupPlan Plan(LedgerSnapshot snapshot, HybridLedgerOptions? options = null) =>
        new CleanupPlanner(Microsoft.Extensions.Options.Options.Create(options ?? TestInventory.Options()), new TestInventory.Clock()).CreatePlan(snapshot);

    [Fact]
    public void PlansIntuneBeforeEntraAndRequiresExplicitOptIn()
    {
        CleanupPlan plan = Plan(TestInventory.Snapshot());
        Assert.Empty(plan.Blockers);
        Assert.Equal(["Intune", "EntraID"], plan.Actions.Select(action => action.Source));
        Assert.Empty(Plan(TestInventory.Snapshot(), new()).Actions);
    }

    [Fact]
    public void MissingAllowlistBlocksEntirePlan()
    {
        HybridLedgerOptions options = TestInventory.Options();
        options.Cleanup.AllowedAzureAdDeviceIds = [];
        Assert.NotEmpty(Plan(TestInventory.Snapshot(), options).Blockers);
    }

    [Theory]
    [InlineData("AzureAd", "Windows")]
    [InlineData("Workplace", "Windows")]
    [InlineData("ServerAd", "Android")]
    public void NeverPlansCloudOnlyOrNonWindowsDevices(string trustType, string os)
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        Assert.Empty(Plan(snapshot with { EntraDevices = [snapshot.EntraDevices[0] with { TrustType = trustType, OperatingSystem = os }] }).Actions);
    }

    [Theory]
    [InlineData("Intune")]
    [InlineData("EntraID")]
    public void FreshCloudActivityPreventsDeletion(string source)
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        snapshot = source == "Intune"
            ? snapshot with { IntuneDevices = [snapshot.IntuneDevices[0] with { LastSyncDateTime = TestInventory.Now }] }
            : snapshot with { EntraDevices = [snapshot.EntraDevices[0] with { LastSyncDateTime = TestInventory.Now }] };
        Assert.Empty(Plan(snapshot).Actions);
    }

    [Theory]
    [InlineData("unknown-intune")]
    [InlineData("unknown-entra")]
    [InlineData("unknown-ad")]
    [InlineData("changed-ad")]
    [InlineData("zero-ad")]
    [InlineData("zero-intune")]
    [InlineData("zero-entra")]
    public void MissingOrRecentEvidencePreventsDeletion(string scenario)
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        snapshot = scenario switch
        {
            "unknown-intune" => snapshot with { IntuneDevices = [snapshot.IntuneDevices[0] with { LastSyncDateTime = null }] },
            "unknown-entra" => snapshot with { EntraDevices = [snapshot.EntraDevices[0] with { LastSyncDateTime = null }] },
            "unknown-ad" => snapshot with { AllActiveDirectoryComputers = [snapshot.AllActiveDirectoryComputers[0] with { LastLogonTimestamp = null }] },
            "zero-ad" => snapshot with { AllActiveDirectoryComputers = [snapshot.AllActiveDirectoryComputers[0] with { LastLogonTimestamp = DateTimeOffset.FromFileTime(0) }] },
            "zero-intune" => snapshot with { IntuneDevices = [snapshot.IntuneDevices[0] with { LastSyncDateTime = DateTimeOffset.MinValue }] },
            "zero-entra" => snapshot with { EntraDevices = [snapshot.EntraDevices[0] with { LastSyncDateTime = DateTimeOffset.UnixEpoch }] },
            _ => snapshot with { AllActiveDirectoryComputers = [snapshot.AllActiveDirectoryComputers[0] with { WhenChanged = TestInventory.Now }] }
        };
        Assert.Empty(Plan(snapshot).Actions);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("empty-ad")]
    [InlineData("empty-intune")]
    [InlineData("ambiguous")]
    [InlineData("slow")]
    public void UnsafeInventoriesBlockEntirePlan(string scenario)
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        snapshot = scenario switch
        {
            "incomplete" => snapshot with { InventoryComplete = false },
            "stale" => snapshot with { CollectionStartedAt = TestInventory.Now.AddDays(-1) },
            "future" => snapshot with { CapturedAt = TestInventory.Now.AddDays(1) },
            "empty-ad" => snapshot with { AllActiveDirectoryComputers = [] },
            "empty-intune" => snapshot with { IntuneDevices = [] },
            "ambiguous" => snapshot with { AmbiguousMatches = 1 },
            _ => snapshot with { CollectionStartedAt = TestInventory.Now.AddHours(-1) }
        };
        Assert.NotEmpty(Plan(snapshot).Blockers);
    }

    [Fact]
    public void NameOnlyAdMatchCannotAuthorizeCloudDeletion()
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        Assert.Empty(Plan(snapshot with
        {
            AllActiveDirectoryComputers = [snapshot.AllActiveDirectoryComputers[0] with { ObjectGuid = Guid.NewGuid() }]
        }).Actions);
    }

    [Fact]
    public void DuplicateCloudIdentitiesBlockEntirePlan()
    {
        LedgerSnapshot snapshot = TestInventory.Snapshot();
        Assert.NotEmpty(Plan(snapshot with
        {
            EntraDevices = [snapshot.EntraDevices[0], snapshot.EntraDevices[0] with { Id = Guid.NewGuid() }]
        }).Blockers);
    }

    [Fact]
    public void CountBudgetRejectsRatherThanTruncatesPlan()
    {
        HybridLedgerOptions options = TestInventory.Options();
        options.Cleanup.MaxDeletesPerRun = 1;
        CleanupPlan plan = Plan(TestInventory.Snapshot(), options);
        Assert.Equal(2, plan.Actions.Count);
        Assert.NotEmpty(plan.Blockers);
    }

    [Fact]
    public void PercentageBudgetRejectsCompletePlan()
    {
        HybridLedgerOptions options = TestInventory.Options();
        options.Cleanup.MaxDeletePercentage = 5;
        Assert.NotEmpty(Plan(TestInventory.Snapshot(), options).Blockers);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    public void ExclusionsOverrideAllowlist(string kind)
    {
        HybridLedgerOptions options = TestInventory.Options();
        if (kind == "id") options.Cleanup.ExcludedAzureAdDeviceIds = [TestInventory.DeviceId];
        else options.Cleanup.ExcludedDeviceNames = ["device01.example.com"];
        Assert.Empty(Plan(TestInventory.Snapshot(), options).Actions);
    }
}
