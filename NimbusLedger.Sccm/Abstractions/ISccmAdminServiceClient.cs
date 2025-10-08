using NimbusLedger.Sccm.Models;

namespace NimbusLedger.Sccm.Abstractions;

public interface ISccmAdminServiceClient
{
    IAsyncEnumerable<CmDevice> GetDevicesAsync(string? odataFilter = null, CancellationToken ct = default);
    Task<bool> DeleteDeviceByResourceIdAsync(int resourceId, CancellationToken ct = default);
}
