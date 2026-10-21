using NimbusLedger.Core.Models;

namespace NimbusLedger.Sccm.Abstractions;

public interface ISccmCleanupService
{
    Task PerformCleanupAsync(LedgerSnapshot snapshot, CancellationToken cancellationToken);
}
