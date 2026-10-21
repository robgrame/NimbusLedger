using NimbusLedger.Core.Models;

namespace NimbusLedger.Core.Abstractions;

public interface IConfigMgrDeviceClient
{
    bool Enabled { get; }
    Task<IReadOnlyList<ConfigMgrDevice>> GetDevicesAsync(CancellationToken cancellationToken);
}
