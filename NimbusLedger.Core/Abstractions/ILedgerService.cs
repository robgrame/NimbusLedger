using NimbusLedger.Core.Models;

namespace NimbusLedger.Core.Abstractions;

public interface ILedgerService
{
    Task<LedgerSnapshot> ReconcileAsync(CancellationToken cancellationToken);
}
