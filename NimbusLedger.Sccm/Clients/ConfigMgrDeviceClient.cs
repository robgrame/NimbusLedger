using Microsoft.Extensions.Options;
using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using NimbusLedger.Sccm.Abstractions;
using NimbusLedger.Sccm.Options;

namespace NimbusLedger.Sccm.Clients;

public sealed class ConfigMgrDeviceClient(
    ISccmAdminServiceClient client,
    IOptions<SccmCleanupOptions> options) : IConfigMgrDeviceClient
{
    public bool Enabled => options.Value.Enabled;

    public async Task<IReadOnlyList<ConfigMgrDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
            throw new InvalidOperationException("ConfigMgr inventory is disabled.");
        List<ConfigMgrDevice> devices = [];
        await foreach (Models.CmDevice device in client.GetDevicesAsync(ct: cancellationToken).ConfigureAwait(false))
            devices.Add(new(device.ResourceId, device.Name!, device.LastActiveTime, device.IsObsolete, device.ClientActiveStatus)
            {
                ActiveDirectoryObjectGuid = device.ActiveDirectoryObjectGuid
            });
        return devices;
    }
}
