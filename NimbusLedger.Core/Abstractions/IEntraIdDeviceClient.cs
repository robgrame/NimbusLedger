using NimbusLedger.Core.Models;

namespace NimbusLedger.Core.Abstractions;

public interface IEntraIdDeviceClient
{
    Task<IReadOnlyList<DeviceRecord>> GetDevicesAsync(CancellationToken cancellationToken);
    Task DeleteDeviceAsync(Guid id, CancellationToken cancellationToken);
}
