using NimbusLedger.Sccm.Abstractions;
using NimbusLedger.Sccm.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NimbusLedger.Sccm.Services;

public sealed class SccmCleanupService : ISccmCleanupService
{
    private readonly ISccmAdminServiceClient _client;
    private readonly SccmCleanupOptions _options;
    private readonly ILogger<SccmCleanupService> _logger;

    public SccmCleanupService(ISccmAdminServiceClient client, IOptions<SccmCleanupOptions> opts, ILogger<SccmCleanupService> logger)
    {
        _client = client;
        _options = opts.Value;
        _logger = logger;
    }

    public async Task<int> CleanupObsoleteAsync(CancellationToken ct = default)
    {
        int deleted = 0;
        await foreach (var d in _client.GetDevicesAsync("IsObsolete eq 1", ct))
        {
            if (await _client.DeleteDeviceByResourceIdAsync(d.ResourceId, ct))
            {
                deleted++;
                _logger.LogInformation("Deleted obsolete SCCM device {Name} ({Id})", d.Name, d.ResourceId);
            }
        }
        return deleted;
    }

    public async Task<int> CleanupInactiveAsync(CancellationToken ct = default)
    {
        // This filter relies on ClientActiveStatus flag. You may extend with last online timestamp if available.
        int deleted = 0;
        await foreach (var d in _client.GetDevicesAsync("ClientActiveStatus eq 0", ct))
        {
            if (await _client.DeleteDeviceByResourceIdAsync(d.ResourceId, ct))
            {
                deleted++;
                _logger.LogInformation("Deleted inactive SCCM device {Name} ({Id})", d.Name, d.ResourceId);
            }
        }
        return deleted;
    }
}
