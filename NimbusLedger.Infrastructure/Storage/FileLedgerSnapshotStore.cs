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

        string? tempFile = null;
        try
        {
            Directory.CreateDirectory(_options.RootPath);
            var latestPath = Path.Combine(_options.RootPath, _options.LatestFileName);
            var historyPath = Path.Combine(_options.RootPath, $"snapshot-{snapshot.CapturedAt:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}.json");

            tempFile = Path.Combine(_options.RootPath, $"tmp-{Guid.NewGuid():N}.json");

            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, _serializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Copy(tempFile, historyPath, overwrite: false);
            await PublishLatestAsync(tempFile, latestPath, cancellationToken).ConfigureAwait(false);

            PruneHistory();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Snapshot persisted to {LatestPath}", latestPath);
            }
        }
        finally
        {
            try
            {
                if (tempFile is not null)
                    File.Delete(tempFile);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Failed to remove incomplete snapshot file.");
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task PublishLatestAsync(string temporaryPath, string latestPath, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(latestPath))
                    File.Replace(temporaryPath, latestPath, destinationBackupFileName: null);
                else
                    File.Move(temporaryPath, latestPath);
                return;
            }
            catch (IOException exception) when (OperatingSystem.IsWindows() && attempt < 7
                && (exception.HResult & 0xffff) is 32 or 33 or 80 or 183 or 1175 or 1176)
            {
                _logger.LogDebug("Atomic snapshot replacement is temporarily locked; retrying publication {Attempt}.", attempt + 1);
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() && attempt < 7)
            {
                _logger.LogDebug("Atomic snapshot replacement temporarily denied access; retrying publication {Attempt}.", attempt + 1);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(20 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<LedgerSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var latestPath = Path.Combine(_options.RootPath, _options.LatestFileName);
            FileStream? stream = await OpenLatestAsync(latestPath, cancellationToken).ConfigureAwait(false);
            if (stream is null)
                return null;
            await using (stream)
                return await JsonSerializer.DeserializeAsync<LedgerSnapshot>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FileStream?> OpenLatestAsync(string path, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (FileNotFoundException) when (attempt == 5)
            {
                return null;
            }
            catch (IOException exception) when (attempt < 5
                && (exception is FileNotFoundException || OperatingSystem.IsWindows() && (exception.HResult & 0xffff) is 32 or 33))
            {
                _logger.LogDebug("Snapshot replacement is in progress; retrying read {Attempt}.", attempt + 1);
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() && attempt < 5)
            {
                _logger.LogDebug("Snapshot replacement temporarily denied access; retrying read {Attempt}.", attempt + 1);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
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
