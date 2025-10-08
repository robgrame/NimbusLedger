using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NimbusLedger.Worker;

public sealed class LedgerWorker : BackgroundService
{
    private readonly ILedgerService _ledgerService;
    private readonly ICleanupService _cleanupService;
    private readonly HybridLedgerOptions _options;
    private readonly ILogger<LedgerWorker> _logger;

    public LedgerWorker(ILedgerService ledgerService, ICleanupService cleanupService, IOptions<HybridLedgerOptions> options, ILogger<LedgerWorker> logger)
    {
        _ledgerService = ledgerService ?? throw new ArgumentNullException(nameof(ledgerService));
        _cleanupService = cleanupService ?? throw new ArgumentNullException(nameof(cleanupService));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

                // Perform optional cleanup according to rules/options
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
