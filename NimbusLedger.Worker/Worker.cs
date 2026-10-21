using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NimbusLedger.Sccm.Abstractions;
using NimbusLedger.Sccm.Options;

namespace NimbusLedger.Worker;

public sealed class LedgerWorker : BackgroundService
{
    private readonly ILedgerService _ledgerService;
    private readonly ICleanupService _cleanupService;
    private readonly HybridLedgerOptions _options;
    private readonly ILogger<LedgerWorker> _logger;
    private readonly IServiceProvider _services;
    private readonly SccmCleanupOptions _configMgrOptions;

    public LedgerWorker(ILedgerService ledgerService, ICleanupService cleanupService, IOptions<HybridLedgerOptions> options,
        ILogger<LedgerWorker> logger, IServiceProvider services, IOptions<SccmCleanupOptions> configMgrOptions)
    {
        _ledgerService = ledgerService ?? throw new ArgumentNullException(nameof(ledgerService));
        _cleanupService = cleanupService ?? throw new ArgumentNullException(nameof(cleanupService));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _services = services;
        _configMgrOptions = configMgrOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(_options.Snapshot.RootPath);
        using FileStream lease = new(Path.Combine(_options.Snapshot.RootPath, ".worker.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        _logger.LogInformation("Worker acquired exclusive execution lease in snapshot directory {Path}", _options.Snapshot.RootPath);
        var startupDelay = _options.Scheduler.StartupDelay;
        if (startupDelay > TimeSpan.Zero)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Ledger worker delaying startup for {Delay}", startupDelay);
            }

            await Task.Delay(startupDelay, stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _ledgerService.ReconcileAsync(stoppingToken).ConfigureAwait(false);

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Ledger snapshot captured with AD={ActiveDirectory}, Entra={Entra}, Intune={Intune}, MissingEntra={MissingEntra}, MissingIntune={MissingIntune}",
                        snapshot.Metrics.ActiveDirectoryCount,
                        snapshot.Metrics.EntraCount,
                        snapshot.Metrics.IntuneCount,
                        snapshot.Metrics.MissingInEntraCount,
                        snapshot.Metrics.MissingInIntuneCount);
                }

                if (_configMgrOptions.Enabled && _configMgrOptions.CleanupEnabled)
                    await _services.GetRequiredService<ISccmCleanupService>()
                        .PerformCleanupAsync(snapshot, stoppingToken).ConfigureAwait(false);
                await _cleanupService.PerformCleanupAsync(snapshot, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ledger reconciliation failed");
            }

            var interval = _options.Scheduler.Interval;
            if (interval <= TimeSpan.Zero)
            {
                interval = TimeSpan.FromMinutes(30);
            }

            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
