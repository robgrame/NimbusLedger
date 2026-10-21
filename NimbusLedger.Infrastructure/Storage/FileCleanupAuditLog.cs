using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Options;

namespace NimbusLedger.Infrastructure.Storage;

public sealed class FileCleanupAuditLog(IOptions<HybridLedgerOptions> options) : ICleanupAuditLog, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task WriteAsync(string source, string objectId, string outcome, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string root = options.Value.Snapshot.RootPath;
            Directory.CreateDirectory(root);
            await using FileStream stream = new(Path.Combine(root, "cleanup-audit.jsonl"),
                FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
            byte[] entry = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow, source, objectId, outcome
            }) + "\n");
            await stream.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
