using NimbusLedger.Core.Models;

namespace NimbusLedger.Core.Abstractions;

public interface IIntuneDeviceClient
{
    Task<IReadOnlyList<DeviceRecord>> GetManagedDevicesAsync(CancellationToken cancellationToken);
    Task DeleteManagedDeviceAsync(Guid id, CancellationToken cancellationToken);
}
