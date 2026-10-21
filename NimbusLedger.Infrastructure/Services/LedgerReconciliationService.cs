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
    private readonly IConfigMgrDeviceClient _configMgrClient;
    private readonly ICleanupPlanner _cleanupPlanner;

    public LedgerReconciliationService(
        IActiveDirectoryClient activeDirectoryClient,
        IEntraIdDeviceClient entraIdDeviceClient,
        IIntuneDeviceClient intuneDeviceClient,
        ILedgerSnapshotStore snapshotStore,
        IOptions<HybridLedgerOptions> options,
        ILogger<LedgerReconciliationService> logger,
        IConfigMgrDeviceClient configMgrClient,
        ICleanupPlanner cleanupPlanner)
    {
        _activeDirectoryClient = activeDirectoryClient ?? throw new ArgumentNullException(nameof(activeDirectoryClient));
        _entraIdDeviceClient = entraIdDeviceClient ?? throw new ArgumentNullException(nameof(entraIdDeviceClient));
        _intuneDeviceClient = intuneDeviceClient ?? throw new ArgumentNullException(nameof(intuneDeviceClient));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configMgrClient = configMgrClient;
        _cleanupPlanner = cleanupPlanner;
    }

    public async Task<LedgerSnapshot> ReconcileAsync(CancellationToken cancellationToken)
    {
        var stopwatch = ValueStopwatch.StartNew();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.ActiveDirectory.ActivityWindowDays);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Starting reconciliation using activity cutoff {Cutoff}", cutoff);
        }

        Task<IReadOnlyList<ComputerAccount>> adTask = _activeDirectoryClient.GetComputersAsync(cancellationToken);
        Task<IReadOnlyList<DeviceRecord>> entraTask = _entraIdDeviceClient.GetDevicesAsync(cancellationToken);
        Task<IReadOnlyList<DeviceRecord>> intuneTask = _intuneDeviceClient.GetManagedDevicesAsync(cancellationToken);
        Task<IReadOnlyList<ConfigMgrDevice>>? configMgrTask = _configMgrClient.Enabled
            ? _configMgrClient.GetDevicesAsync(cancellationToken) : null;
        List<Task> collections = [adTask, entraTask, intuneTask];
        if (configMgrTask is not null) collections.Add(configMgrTask);
        await Task.WhenAll(collections).ConfigureAwait(false);
        IReadOnlyList<ComputerAccount> adComputers = await adTask.ConfigureAwait(false);
        IReadOnlyList<DeviceRecord> entraDevices = await entraTask.ConfigureAwait(false);
        IReadOnlyList<DeviceRecord> intuneDevices = await intuneTask.ConfigureAwait(false);
        IReadOnlyList<ConfigMgrDevice>? configMgrDevices = configMgrTask is null
            ? null : await configMgrTask.ConfigureAwait(false);

        if (adComputers.Select(device => device.ObjectGuid).Distinct().Count() != adComputers.Count
            || entraDevices.Select(device => device.Id).Distinct().Count() != entraDevices.Count
            || intuneDevices.Select(device => device.Id).Distinct().Count() != intuneDevices.Count
            || configMgrDevices?.Select(device => device.ResourceId).Distinct().Count() != configMgrDevices?.Count)
            throw new InvalidDataException("Inventory contains duplicate source object IDs.");

        var activeComputers = adComputers
            .Where(c => c.LastLogonTimestamp is not null && c.LastLogonTimestamp >= cutoff)
            .ToList();

        var staleCount = adComputers.Count - activeComputers.Count;

        DeviceIdentityIndex entraLookup = new(entraDevices.Select(DeviceIdentity.From));
        DeviceIdentityIndex intuneLookup = new(intuneDevices.Select(DeviceIdentity.From));
        DeviceIdentityIndex? configMgrLookup = configMgrDevices is null ? null : new(configMgrDevices.Select(DeviceIdentity.From));
        List<ComputerAccount> missingInEntra = [];
        List<ComputerAccount> missingInIntune = [];
        List<ComputerAccount>? missingInConfigMgr = configMgrDevices is null ? null : [];
        int ambiguous = 0;
        int nameOnly = 0;
        foreach (ComputerAccount computer in activeComputers)
        {
            Check(entraLookup, computer, missingInEntra);
            Check(intuneLookup, computer, missingInIntune);
            if (configMgrLookup is not null)
                Check(configMgrLookup, computer, missingInConfigMgr!);
        }

        void Check(DeviceIdentityIndex index, ComputerAccount computer, List<ComputerAccount> missing)
        {
            DeviceMatchKind kind = index.Match(DeviceIdentity.From(computer));
            if (kind is DeviceMatchKind.Missing or DeviceMatchKind.Ambiguous)
                missing.Add(computer);
            if (kind == DeviceMatchKind.Ambiguous)
                ambiguous++;
            if (kind == DeviceMatchKind.NameOnly)
                nameOnly++;
        }

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
            metrics)
        {
            CollectionStartedAt = startedAt,
            InventoryComplete = true,
            AllActiveDirectoryComputers = adComputers,
            ConfigMgrDevices = configMgrDevices,
            MissingInConfigMgr = missingInConfigMgr,
            AmbiguousMatches = ambiguous,
            NameOnlyMatches = nameOnly
        };
        snapshot = snapshot with { CleanupPlan = _cleanupPlanner.CreatePlan(snapshot) };

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
