using System.Text.Json;
using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NimbusLedger.Infrastructure.Storage;

public sealed class FileLedgerSnapshotStore : ILedgerSnapshotStore, IDisposable
{
    private readonly SnapshotOptions _options;
    private readonly ILogger<FileLedgerSnapshotStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileLedgerSnapshotStore(IOptions<HybridLedgerOptions> options, ILogger<FileLedgerSnapshotStore> logger)
    {
        _options = options.Value.Snapshot;
        _logger = logger;
    }

    public async Task SaveSnapshotAsync(LedgerSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(_options.RootPath);
            var latestPath = Path.Combine(_options.RootPath, _options.LatestFileName);
            var historyPath = Path.Combine(_options.RootPath, $"snapshot-{snapshot.CapturedAt:yyyyMMddHHmmss}.json");

            var tempFile = Path.Combine(_options.RootPath, $"tmp-{Guid.NewGuid():N}.json");

            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, _serializerOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Copy(tempFile, latestPath, overwrite: true);
            File.Copy(tempFile, historyPath, overwrite: false);
            File.Delete(tempFile);

            PruneHistory();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Snapshot persisted to {LatestPath}", latestPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LedgerSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var latestPath = Path.Combine(_options.RootPath, _options.LatestFileName);
            if (!File.Exists(latestPath))
            {
                return null;
            }

            await using var stream = File.OpenRead(latestPath);
            return await JsonSerializer.DeserializeAsync<LedgerSnapshot>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PruneHistory()
    {
        if (_options.HistorySize <= 0)
        {
            return;
        }

        var historyFiles = Directory
            .EnumerateFiles(_options.RootPath, "snapshot-*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetCreationTimeUtc)
            .Skip(_options.HistorySize)
            .ToList();

        foreach (var file in historyFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(ex, "Failed to delete history snapshot {File}", file);
                }
            }
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
