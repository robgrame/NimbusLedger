using System.Diagnostics;
using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NimbusLedger.Infrastructure.Services;

public sealed class LedgerReconciliationService : ILedgerService
{
    private readonly IActiveDirectoryClient _activeDirectoryClient;
    private readonly IEntraIdDeviceClient _entraIdDeviceClient;
    private readonly IIntuneDeviceClient _intuneDeviceClient;
    private readonly ILedgerSnapshotStore _snapshotStore;
    private readonly HybridLedgerOptions _options;
    private readonly ILogger<LedgerReconciliationService> _logger;

    public LedgerReconciliationService(
        IActiveDirectoryClient activeDirectoryClient,
        IEntraIdDeviceClient entraIdDeviceClient,
        IIntuneDeviceClient intuneDeviceClient,
        ILedgerSnapshotStore snapshotStore,
        IOptions<HybridLedgerOptions> options,
        ILogger<LedgerReconciliationService> logger)
    {
        _activeDirectoryClient = activeDirectoryClient ?? throw new ArgumentNullException(nameof(activeDirectoryClient));
        _entraIdDeviceClient = entraIdDeviceClient ?? throw new ArgumentNullException(nameof(entraIdDeviceClient));
        _intuneDeviceClient = intuneDeviceClient ?? throw new ArgumentNullException(nameof(intuneDeviceClient));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LedgerSnapshot> ReconcileAsync(CancellationToken cancellationToken)
    {
        var stopwatch = ValueStopwatch.StartNew();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.ActiveDirectory.ActivityWindowDays);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Starting reconciliation using activity cutoff {Cutoff}", cutoff);
        }

        var adComputers = await _activeDirectoryClient.GetComputersAsync(cancellationToken).ConfigureAwait(false);
        var entraDevices = await _entraIdDeviceClient.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        var intuneDevices = await _intuneDeviceClient.GetManagedDevicesAsync(cancellationToken).ConfigureAwait(false);

        var activeComputers = adComputers
            .Where(c => c.LastLogonTimestamp is not null && c.LastLogonTimestamp >= cutoff)
            .ToList();

        var staleCount = adComputers.Count - activeComputers.Count;

        var entraLookup = BuildLookup(entraDevices);
        var intuneLookup = BuildLookup(intuneDevices);

        var missingInEntra = activeComputers.Where(computer => !ExistsInLookup(computer, entraLookup)).ToList();
        var missingInIntune = activeComputers.Where(computer => !ExistsInLookup(computer, intuneLookup)).ToList();

        var metrics = new LedgerSnapshotMetrics(
            activeComputers.Count,
            entraDevices.Count,
            intuneDevices.Count,
            missingInEntra.Count,
            missingInIntune.Count,
            staleCount);

        var snapshot = new LedgerSnapshot(
            DateTimeOffset.UtcNow,
            activeComputers,
            entraDevices,
            intuneDevices,
            missingInEntra,
            missingInIntune,
            metrics);

        await _snapshotStore.SaveSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Reconciliation finished in {ElapsedMs} ms. ActiveDirectory={AdCount}, Entra={EntraCount}, Intune={IntuneCount}, MissingEntra={MissingEntra}, MissingIntune={MissingIntune}",
                stopwatch.GetElapsedTime().TotalMilliseconds,
                metrics.ActiveDirectoryCount,
                metrics.EntraCount,
                metrics.IntuneCount,
                metrics.MissingInEntraCount,
                metrics.MissingInIntuneCount);
        }

        return snapshot;
    }

    private static DeviceLookup BuildLookup(IReadOnlyList<DeviceRecord> devices)
    {
        var azureIds = new HashSet<Guid>();
        var displayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            if (device.AzureAdDeviceId.HasValue)
            {
                azureIds.Add(device.AzureAdDeviceId.Value);
            }

            if (!string.IsNullOrWhiteSpace(device.DisplayName))
            {
                displayNames.Add(device.DisplayName);
            }
        }

        return new DeviceLookup(azureIds, displayNames);
    }

    private static bool ExistsInLookup(ComputerAccount account, DeviceLookup lookup)
    {
        if (lookup.AzureDeviceIds.Contains(account.ObjectGuid))
        {
            return true;
        }

        if (account.AzureAdDeviceId.HasValue && lookup.AzureDeviceIds.Contains(account.AzureAdDeviceId.Value))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(account.DnsHostName) && lookup.DisplayNames.Contains(account.DnsHostName))
        {
            return true;
        }

        return lookup.DisplayNames.Contains(account.SamAccountName);
    }

    private readonly record struct DeviceLookup(HashSet<Guid> AzureDeviceIds, HashSet<string> DisplayNames);

    private readonly struct ValueStopwatch
    {
        private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;
        private readonly long _startTimestamp;

        private ValueStopwatch(long startTimestamp) => _startTimestamp = startTimestamp;

        public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

        public TimeSpan GetElapsedTime()
        {
            var end = Stopwatch.GetTimestamp();
            return TimeSpan.FromTicks((long)((end - _startTimestamp) * TimestampToTicks));
        }
    }
}
