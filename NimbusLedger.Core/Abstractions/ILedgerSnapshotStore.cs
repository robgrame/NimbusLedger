using NimbusLedger.Core.Models;

namespace NimbusLedger.Core.Abstractions;

public interface ILedgerSnapshotStore
{
    Task SaveSnapshotAsync(LedgerSnapshot snapshot, CancellationToken cancellationToken);

    Task<LedgerSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken);
}
