using NimbusLedger.Core.Models;

namespace NimbusLedger.Core.Abstractions;

public interface ICleanupService
{
    Task PerformCleanupAsync(LedgerSnapshot snapshot, CancellationToken cancellationToken);
}
