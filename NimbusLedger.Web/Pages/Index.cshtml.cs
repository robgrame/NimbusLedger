using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NimbusLedger.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ILedgerSnapshotStore _snapshotStore;
    private readonly HybridLedgerOptions _options;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ILedgerSnapshotStore snapshotStore, IOptions<HybridLedgerOptions> options, ILogger<IndexModel> logger)
    {
        _snapshotStore = snapshotStore;
        _options = options.Value;
        _logger = logger;
    }

    public LedgerSnapshot? Snapshot { get; private set; }

    public LedgerSnapshotMetrics? Metrics => Snapshot?.Metrics;

    public int ActivityWindowDays => _options.ActiveDirectory.ActivityWindowDays;
    public bool SnapshotIsCurrent => Snapshot is not null && Snapshot.InventoryComplete
        && Snapshot.CollectionStartedAt != default && Snapshot.CapturedAt >= Snapshot.CollectionStartedAt
        && Snapshot.CapturedAt <= DateTimeOffset.UtcNow
        && DateTimeOffset.UtcNow - Snapshot.CollectionStartedAt <= _options.Snapshot.MaxAge;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _snapshotStore.GetLatestSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (Snapshot is null && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("No ledger snapshot available for display");
        }

        return Page();
    }
}
