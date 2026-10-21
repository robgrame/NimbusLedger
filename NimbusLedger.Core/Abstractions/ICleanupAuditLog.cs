namespace NimbusLedger.Core.Abstractions;

public interface ICleanupAuditLog
{
    Task WriteAsync(string source, string objectId, string outcome, CancellationToken cancellationToken);
}
