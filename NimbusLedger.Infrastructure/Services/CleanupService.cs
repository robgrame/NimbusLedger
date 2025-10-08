using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NimbusLedger.Infrastructure.Services;

public sealed class CleanupService : ICleanupService
{
    private readonly IEntraIdDeviceClient _entra;
    private readonly IIntuneDeviceClient _intune;
    private readonly HybridLedgerOptions _options;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(IEntraIdDeviceClient entra, IIntuneDeviceClient intune, IOptions<HybridLedgerOptions> options, ILogger<CleanupService> logger)
    {
        _entra = entra;
        _intune = intune;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PerformCleanupAsync(LedgerSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!_options.Cleanup.Enabled)
        {
            _logger.LogInformation("Cleanup disabled. Skipping.");
            return;
        }

        var intuneFreshCutoff = DateTimeOffset.UtcNow.AddDays(-_options.Cleanup.IntuneFreshWindowDays);

        // Build quick lookups by AzureADDeviceId for Intune recency check
        var intuneByAzureId = snapshot.IntuneDevices
            .Where(d => d.AzureAdDeviceId.HasValue)
            .GroupBy(d => d.AzureAdDeviceId!.Value)
            .ToDictionary(g => g.Key, g => g.Max(d => d.LastSyncDateTime));

        // Protective rule on cloud deletions: Only delete when Intune is NOT fresh.
        if (_options.Cleanup.DeleteEntra)
        {
            foreach (var entra in snapshot.EntraDevices)
            {
                bool adActive = IsAdActiveCounterpartPresent(entra, snapshot.ActiveDirectoryComputers);
                if (adActive) continue;

                bool intuneFresh = entra.AzureAdDeviceId.HasValue
                    && intuneByAzureId.TryGetValue(entra.AzureAdDeviceId.Value, out var lastSync)
                    && lastSync.HasValue && lastSync.Value >= intuneFreshCutoff;

                if (intuneFresh)
                {
                    _logger.LogWarning("Inconsistency: AD stale/missing but Intune fresh for AzureAdDevice {AzureId}; Entra deletion suppressed", entra.AzureAdDeviceId);
                    continue;
                }

                if (!_options.Cleanup.DryRun)
                {
                    await _entra.DeleteDeviceAsync(entra.Id, cancellationToken).ConfigureAwait(false);
                }
                _logger.LogInformation("{Action} Entra device {Name} ({Id}) due to stale/missing AD and no fresh Intune check-in",
                    _options.Cleanup.DryRun ? "Would delete" : "Deleted", entra.DisplayName, entra.Id);
            }
        }

        if (_options.Cleanup.DeleteIntune)
        {
            foreach (var md in snapshot.IntuneDevices)
            {
                bool adActive = IsAdActiveCounterpartPresent(md, snapshot.ActiveDirectoryComputers);
                if (adActive) continue;

                bool intuneFresh = md.LastSyncDateTime.HasValue && md.LastSyncDateTime.Value >= intuneFreshCutoff;
                if (intuneFresh)
                {
                    _logger.LogWarning("Inconsistency: AD stale/missing but Intune fresh for ManagedDevice {Id}; Intune deletion suppressed", md.Id);
                    continue;
                }

                if (!_options.Cleanup.DryRun)
                {
                    await _intune.DeleteManagedDeviceAsync(md.Id, cancellationToken).ConfigureAwait(false);
                }
                _logger.LogInformation("{Action} Intune device {Name} ({Id}) due to stale/missing AD and no fresh Intune check-in",
                    _options.Cleanup.DryRun ? "Would delete" : "Deleted", md.DisplayName, md.Id);
            }
        }
    }

    private static bool IsAdActiveCounterpartPresent(DeviceRecord cloud, IReadOnlyList<ComputerAccount> activeAd)
    {
        if (cloud.AzureAdDeviceId.HasValue && activeAd.Any(a => a.AzureAdDeviceId == cloud.AzureAdDeviceId))
            return true;
        if (!string.IsNullOrWhiteSpace(cloud.DisplayName) && activeAd.Any(a => string.Equals(a.DnsHostName, cloud.DisplayName, StringComparison.OrdinalIgnoreCase) || string.Equals(a.SamAccountName, cloud.DisplayName, StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }
}
