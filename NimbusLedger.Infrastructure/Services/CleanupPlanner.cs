using Microsoft.Extensions.Options;
using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;

namespace NimbusLedger.Infrastructure.Services;

public sealed class CleanupPlanner(IOptions<HybridLedgerOptions> options, TimeProvider clock) : ICleanupPlanner
{
    public CleanupPlan CreatePlan(LedgerSnapshot snapshot)
    {
        DateTimeOffset now = clock.GetUtcNow();
        CleanupOptions settings = options.Value.Cleanup;
        List<string> blockers = [];
        List<CleanupAction> actions = [];
        if (!settings.Enabled)
            return new(now, actions, blockers);

        if (!snapshot.InventoryComplete)
            blockers.Add("Inventory completeness is not established.");
        if (snapshot.CollectionStartedAt == default || snapshot.CapturedAt < snapshot.CollectionStartedAt
            || snapshot.CapturedAt > now || now - snapshot.CollectionStartedAt > settings.MaxSnapshotAge
            || snapshot.CapturedAt - snapshot.CollectionStartedAt > settings.MaxCollectionDuration)
            blockers.Add("Inventory is stale, future-dated or exceeded the collection deadline.");
        if (snapshot.AllActiveDirectoryComputers.Count < settings.MinimumAdDevices
            || snapshot.EntraDevices.Count < settings.MinimumEntraDevices
            || snapshot.IntuneDevices.Count < settings.MinimumIntuneDevices)
            blockers.Add("An inventory is below its configured minimum size.");
        if (settings.AllowedAzureAdDeviceIds.Length == 0)
            blockers.Add("An explicit device-ID allowlist is required.");
        if (snapshot.AmbiguousMatches > 0)
            blockers.Add("Inventory contains ambiguous identity matches.");
        if (blockers.Count > 0)
            return new(now, actions, blockers);

        DeviceIdentityIndex adIndex = new(snapshot.AllActiveDirectoryComputers.Select(DeviceIdentity.From));
        DateTimeOffset adCutoff = now.AddDays(-options.Value.ActiveDirectory.ActivityWindowDays);
        DateTimeOffset freshCutoff = now.AddDays(-settings.IntuneFreshWindowDays);
        HashSet<Guid> allowed = settings.AllowedAzureAdDeviceIds.ToHashSet();
        HashSet<Guid> excluded = settings.ExcludedAzureAdDeviceIds.ToHashSet();
        HashSet<string> excludedNames = new(settings.ExcludedDeviceNames.Select(DeviceIdentityIndex.NormalizeName), StringComparer.OrdinalIgnoreCase);

        foreach (DeviceRecord entra in snapshot.EntraDevices)
        {
            if (entra.AzureAdDeviceId is not Guid azureId || azureId == Guid.Empty || !allowed.Contains(azureId)
                || excluded.Contains(azureId) || excludedNames.Contains(DeviceIdentityIndex.NormalizeName(entra.DisplayName))
                || !string.Equals(entra.TrustType, "ServerAd", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(entra.OperatingSystem, "Windows", StringComparison.OrdinalIgnoreCase))
                continue;

            if (snapshot.EntraDevices.Count(device => device.AzureAdDeviceId == azureId) != 1)
            {
                blockers.Add("Duplicate Entra device identities require manual review.");
                continue;
            }
            if (adIndex.Match(DeviceIdentity.From(entra)) != DeviceMatchKind.StableId)
                continue;
            ComputerAccount[] counterparts = snapshot.AllActiveDirectoryComputers
                .Where(device => device.ObjectGuid == azureId || device.AzureAdDeviceId == azureId).ToArray();
            if (counterparts.Length != 1 || !DeviceActivity.IsKnownOlderThan(counterparts[0].LastLogonTimestamp, adCutoff)
                || !DeviceActivity.IsKnownOlderThan(counterparts[0].WhenChanged, adCutoff))
                continue;

            DeviceRecord[] managed = snapshot.IntuneDevices.Where(device => device.AzureAdDeviceId == azureId).ToArray();
            if (managed.Length != 1)
            {
                if (managed.Length > 1)
                    blockers.Add("Duplicate Intune device identities require manual review.");
                continue;
            }
            DeviceRecord intune = managed[0];
            if (!DeviceActivity.IsKnownOlderThan(entra.LastSyncDateTime, freshCutoff)
                || !DeviceActivity.IsKnownOlderThan(intune.LastSyncDateTime, freshCutoff)
                || !string.Equals(intune.OperatingSystem, "Windows", StringComparison.OrdinalIgnoreCase)
                || excludedNames.Contains(DeviceIdentityIndex.NormalizeName(intune.DisplayName))
                || excludedNames.Contains(DeviceIdentityIndex.NormalizeName(counterparts[0].SamAccountName))
                || excludedNames.Contains(DeviceIdentityIndex.NormalizeName(counterparts[0].DnsHostName ?? string.Empty)))
                continue;

            if (settings.DeleteIntune)
                actions.Add(new("Intune", intune.Id, azureId));
            if (settings.DeleteEntra)
                actions.Add(new("EntraID", entra.Id, azureId));
        }
        int population = snapshot.EntraDevices.Count(device => device.AzureAdDeviceId.HasValue && allowed.Contains(device.AzureAdDeviceId.Value));
        if (actions.Count > settings.MaxDeletesPerRun
            || actions.Select(action => action.AzureAdDeviceId).Distinct().Count() * 100.0 > population * settings.MaxDeletePercentage)
            blockers.Add("The complete plan exceeds its deletion budget; no partial plan will execute.");
        if (actions.Any(action => action.DeviceId == Guid.Empty))
            blockers.Add("A deletion target has an invalid object ID.");
        return new(now, actions, blockers);
    }
}
