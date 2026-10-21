using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using Microsoft.Graph;
using Microsoft.Extensions.Logging;

namespace NimbusLedger.Infrastructure.Graph;

public sealed class GraphIntuneDeviceClient : IIntuneDeviceClient
{
    private static readonly string[] SelectFields =
    [
        "id",
        "azureADDeviceId",
        "deviceName",
        "operatingSystem",
        "osVersion",
        "lastSyncDateTime"
    ];

    private readonly GraphServiceClient _graphClient;
    private readonly ILogger<GraphIntuneDeviceClient> _logger;

    public GraphIntuneDeviceClient(GraphServiceClient graphClient, ILogger<GraphIntuneDeviceClient> logger)
    {
        _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<DeviceRecord>> GetManagedDevicesAsync(CancellationToken cancellationToken)
    {
        var devices = new List<DeviceRecord>();

        var response = await _graphClient.DeviceManagement.ManagedDevices.GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Top = 999;
            requestConfiguration.QueryParameters.Select = SelectFields;
        }, cancellationToken).ConfigureAwait(false);

        HashSet<string> visited = [];
        if (response is null)
            throw new InvalidDataException("Intune inventory returned an empty response.");
        while (response is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (response.Value is not null)
            {
                foreach (var device in response.Value)
                {
                    if (!Guid.TryParse(device.Id, out var deviceId))
                    {
                        throw new InvalidDataException("Intune inventory contains an invalid object ID.");
                    }

                    Guid? azureDeviceId = null;
                    if (!string.IsNullOrWhiteSpace(device.AzureADDeviceId) && Guid.TryParse(device.AzureADDeviceId, out var parsedDeviceId))
                    {
                        azureDeviceId = parsedDeviceId;
                    }

                    var record = new DeviceRecord(
                        deviceId,
                        device.DeviceName ?? string.Empty,
                        azureDeviceId,
                        device.OperatingSystem,
                        device.OsVersion,
                        device.LastSyncDateTime,
                        "Intune");

                    devices.Add(record);
                }
            }

            if (string.IsNullOrEmpty(response.OdataNextLink))
            {
                break;
            }

            if (!visited.Add(response.OdataNextLink))
                throw new InvalidDataException("Intune inventory repeated a pagination link.");
            response = await _graphClient.DeviceManagement.ManagedDevices.WithUrl(response.OdataNextLink).GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Intune inventory pagination returned an empty response.");
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Fetched {Count} managed devices from Intune", devices.Count);
        }

        return devices;
    }

    public async Task DeleteManagedDeviceAsync(Guid id, CancellationToken cancellationToken)
    {
        await _graphClient.DeviceManagement.ManagedDevices[id.ToString()].DeleteAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
        {
            _logger.LogInformation("Deleted Intune managed device {Id}", id);
        }
    }
}
