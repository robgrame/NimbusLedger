using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using NimbusLedger.Sccm.Abstractions;
using NimbusLedger.Sccm.Options;

namespace NimbusLedger.Sccm.Services;

public sealed class SccmCleanupService(
    ISccmAdminServiceClient client,
    ILedgerService ledgerService,
    IOptions<SccmCleanupOptions> options,
    IOptions<HybridLedgerOptions> ledgerOptions,
    ILogger<SccmCleanupService> logger,
    ICleanupAuditLog auditLog) : ISccmCleanupService
{
    public async Task PerformCleanupAsync(LedgerSnapshot snapshot, CancellationToken cancellationToken)
    {
        SccmCleanupOptions settings = options.Value;
        HybridLedgerOptions ledger = ledgerOptions.Value;
        if (!settings.Enabled || !settings.CleanupEnabled || !ledger.Cleanup.Enabled)
        {
            logger.LogInformation("ConfigMgr cleanup disabled.");
            return;
        }
        (List<ConfigMgrDevice> actions, List<string> blockers) = CreatePlan(snapshot);
        if (blockers.Count > 0)
        {
            logger.LogWarning("ConfigMgr cleanup blocked: {Reasons}", string.Join(" ", blockers));
            return;
        }
        logger.LogInformation("ConfigMgr cleanup plan contains resources {ResourceIds}", actions.Select(device => device.ResourceId).ToArray());
        if (!settings.DryRun && !ledger.Cleanup.DryRun && actions.Count > 0)
        {
            LedgerSnapshot current = await ledgerService.ReconcileAsync(cancellationToken).ConfigureAwait(false);
            (List<ConfigMgrDevice> fresh, List<string> freshBlockers) = CreatePlan(current);
            if (freshBlockers.Count > 0 || !actions.ToHashSet().SetEquals(fresh))
                throw new InvalidOperationException("ConfigMgr cleanup preflight changed or failed; no actions executed.");
        }
        foreach (ConfigMgrDevice device in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.DryRun || ledger.Cleanup.DryRun)
            {
                logger.LogInformation("Would delete ConfigMgr resource {ResourceId}; AD object {ObjectGuid}", device.ResourceId, device.ActiveDirectoryObjectGuid);
                continue;
            }
            logger.LogWarning("Executing approved-scope deletion of ConfigMgr resource {ResourceId}", device.ResourceId);
            string objectId = device.ResourceId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await auditLog.WriteAsync("ConfigMgr", objectId, "Started", cancellationToken).ConfigureAwait(false);
            bool deleted;
            try
            {
                deleted = await client.DeleteDeviceByResourceIdAsync(device.ResourceId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                try
                {
                    await auditLog.WriteAsync("ConfigMgr", objectId, "OutcomeUnknown", CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception auditException) when (auditException is IOException or UnauthorizedAccessException)
                {
                    logger.LogError(auditException, "Could not record an uncertain ConfigMgr outcome; inspect the Started audit entry.");
                }
                logger.LogError(exception, "ConfigMgr deletion failed or was interrupted; backend outcome must be verified.");
                throw;
            }
            await auditLog.WriteAsync("ConfigMgr", objectId, deleted ? "Completed" : "NotFound", cancellationToken).ConfigureAwait(false);
            logger.LogInformation("ConfigMgr deletion result: resource {ResourceId}, deleted {Deleted}", device.ResourceId, deleted);
        }
    }

    public (List<ConfigMgrDevice> Actions, List<string> Blockers) CreatePlan(LedgerSnapshot snapshot)
    {
        SccmCleanupOptions settings = options.Value;
        HybridLedgerOptions ledger = ledgerOptions.Value;
        List<string> blockers = [];
        List<ConfigMgrDevice> actions = [];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!snapshot.InventoryComplete || snapshot.ConfigMgrDevices is null
            || snapshot.ConfigMgrDevices.Count == 0 || snapshot.AllActiveDirectoryComputers.Count < ledger.Cleanup.MinimumAdDevices
            || snapshot.EntraDevices.Count < ledger.Cleanup.MinimumEntraDevices
            || snapshot.IntuneDevices.Count < ledger.Cleanup.MinimumIntuneDevices
            || snapshot.CollectionStartedAt == default || snapshot.CapturedAt < snapshot.CollectionStartedAt
            || snapshot.CapturedAt > now || now - snapshot.CollectionStartedAt > ledger.Cleanup.MaxSnapshotAge
            || snapshot.CapturedAt - snapshot.CollectionStartedAt > ledger.Cleanup.MaxCollectionDuration
            || snapshot.AmbiguousMatches > 0 || settings.AllowedResourceIds.Length == 0)
        {
            blockers.Add("Incomplete/stale inventory, ambiguity or missing explicit resource-ID scope.");
            return (actions, blockers);
        }
        foreach (ConfigMgrDevice device in snapshot.ConfigMgrDevices)
        {
            if (!settings.AllowedResourceIds.Contains(device.ResourceId) || settings.ExcludedResourceIds.Contains(device.ResourceId)
                || device.ActiveDirectoryObjectGuid is not Guid adId || adId == Guid.Empty)
                continue;
            if (snapshot.ConfigMgrDevices.Count(record => record.ActiveDirectoryObjectGuid == adId) != 1)
            {
                blockers.Add("Duplicate ConfigMgr AD identities require manual review.");
                continue;
            }
            ComputerAccount[] ad = snapshot.AllActiveDirectoryComputers.Where(account => account.ObjectGuid == adId).ToArray();
            DateTimeOffset adCutoff = now.AddDays(-ledger.ActiveDirectory.ActivityWindowDays);
            if (ad.Length != 1 || !DeviceActivity.IsKnownOlderThan(ad[0].LastLogonTimestamp, adCutoff)
                || !DeviceActivity.IsKnownOlderThan(ad[0].WhenChanged, adCutoff))
                continue;
            Guid azureId = ad[0].AzureAdDeviceId ?? ad[0].ObjectGuid;
            string[] names = [device.Name, ad[0].SamAccountName, ad[0].DnsHostName ?? string.Empty];
            if (ledger.Cleanup.ExcludedAzureAdDeviceIds.Contains(azureId)
                || ledger.Cleanup.ExcludedAzureAdDeviceIds.Contains(adId)
                || ledger.Cleanup.ExcludedDeviceNames.Any(excluded => names.Any(name =>
                    string.Equals(DeviceIdentityIndex.NormalizeName(name), DeviceIdentityIndex.NormalizeName(excluded), StringComparison.OrdinalIgnoreCase))))
                continue;
            DeviceRecord[] entra = snapshot.EntraDevices.Where(record => record.AzureAdDeviceId == azureId).ToArray();
            DeviceRecord[] intune = snapshot.IntuneDevices.Where(record => record.AzureAdDeviceId == azureId).ToArray();
            DateTimeOffset freshCutoff = now.AddDays(-ledger.Cleanup.IntuneFreshWindowDays);
            if (entra.Length != 1 || intune.Length != 1
                || !string.Equals(entra[0].TrustType, "ServerAd", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(entra[0].OperatingSystem, "Windows", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(intune[0].OperatingSystem, "Windows", StringComparison.OrdinalIgnoreCase)
                || !DeviceActivity.IsKnownOlderThan(entra[0].LastSyncDateTime, freshCutoff)
                || !DeviceActivity.IsKnownOlderThan(intune[0].LastSyncDateTime, freshCutoff))
                continue;
            int days = device.IsObsolete ? settings.ObsoleteDaysThreshold : settings.InactiveDaysThreshold;
            if ((!device.IsObsolete && device.ClientActiveStatus != 0)
                || !DeviceActivity.IsKnownOlderThan(device.LastActiveTime, now.AddDays(-days)))
                continue;
            actions.Add(device);
        }
        int population = snapshot.ConfigMgrDevices.Count(device => settings.AllowedResourceIds.Contains(device.ResourceId));
        if (actions.Count > settings.MaxDeletesPerRun || actions.Count * 100.0 > population * ledger.Cleanup.MaxDeletePercentage)
            blockers.Add("Complete plan exceeds its deletion budget; no partial plan will execute.");
        return (actions, blockers);
    }
}
