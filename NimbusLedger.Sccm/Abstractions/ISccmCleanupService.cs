namespace NimbusLedger.Sccm.Abstractions;

public interface ISccmCleanupService
{
    Task<int> CleanupObsoleteAsync(CancellationToken ct = default);
    Task<int> CleanupInactiveAsync(CancellationToken ct = default);
}
