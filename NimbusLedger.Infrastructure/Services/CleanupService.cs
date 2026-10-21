using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NimbusLedger.Infrastructure.Services;

public sealed class CleanupService(
    IEntraIdDeviceClient entra,
    IIntuneDeviceClient intune,
    IActiveDirectoryClient activeDirectory,
    ICleanupPlanner planner,
    IOptions<HybridLedgerOptions> options,
    ILogger<CleanupService> logger,
    ICleanupAuditLog auditLog) : ICleanupService
{
    public async Task PerformCleanupAsync(LedgerSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!options.Value.Cleanup.Enabled)
        {
            logger.LogInformation("Cloud cleanup disabled.");
            return;
        }
        CleanupPlan plan = planner.CreatePlan(snapshot);
        if (plan.Blockers.Count > 0)
        {
            logger.LogWarning("Cloud cleanup blocked: {Reasons}", string.Join(" ", plan.Blockers));
            return;
        }
        if (!options.Value.Cleanup.DryRun && plan.Actions.Count > 0)
        {
            DateTimeOffset started = DateTimeOffset.UtcNow;
            IReadOnlyList<ComputerAccount> ad = await activeDirectory.GetComputersAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<DeviceRecord> cloud = await entra.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<DeviceRecord> managed = await intune.GetManagedDevicesAsync(cancellationToken).ConfigureAwait(false);
            LedgerSnapshot revalidated = snapshot with
            {
                CollectionStartedAt = started,
                CapturedAt = DateTimeOffset.UtcNow,
                AllActiveDirectoryComputers = ad,
                EntraDevices = cloud,
                IntuneDevices = managed
            };
            CleanupPlan fresh = planner.CreatePlan(revalidated);
            if (fresh.Blockers.Count > 0 || !plan.Actions.ToHashSet().SetEquals(fresh.Actions))
                throw new InvalidOperationException("Cloud cleanup preflight changed or failed; no actions executed.");
        }
        foreach (CleanupAction action in plan.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.Value.Cleanup.DryRun)
            {
                logger.LogInformation("Would delete {Source} object {ObjectId}; device {DeviceId}", action.Source, action.DeviceId, action.AzureAdDeviceId);
                continue;
            }
            logger.LogWarning("Executing approved-scope deletion of {Source} object {ObjectId}", action.Source, action.DeviceId);
            await auditLog.WriteAsync(action.Source, action.DeviceId.ToString(), "Started", cancellationToken).ConfigureAwait(false);
            try
            {
                if (action.Source == "Intune")
                    await intune.DeleteManagedDeviceAsync(action.DeviceId, cancellationToken).ConfigureAwait(false);
                else if (action.Source == "EntraID")
                    await entra.DeleteDeviceAsync(action.DeviceId, cancellationToken).ConfigureAwait(false);
                else
                    throw new InvalidOperationException("Unsupported cleanup action source.");
            }
            catch (Exception exception) when (exception is Microsoft.Kiota.Abstractions.ApiException or HttpRequestException or OperationCanceledException)
            {
                try
                {
                    await auditLog.WriteAsync(action.Source, action.DeviceId.ToString(), "OutcomeUnknown", CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception auditException) when (auditException is IOException or UnauthorizedAccessException)
                {
                    logger.LogError(auditException, "Could not record an uncertain cleanup outcome; inspect the Started audit entry.");
                }
                logger.LogError(exception, "Deletion failed or was interrupted; backend outcome must be verified.");
                throw;
            }
            await auditLog.WriteAsync(action.Source, action.DeviceId.ToString(), "Completed", cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Deleted {Source} object {ObjectId}", action.Source, action.DeviceId);
        }
    }
}
