using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Extensions.Logging;

namespace NimbusLedger.Infrastructure.Graph;

public sealed class GraphEntraIdDeviceClient : IEntraIdDeviceClient
{
    private static readonly string[] SelectFields =
    [
        "id",
        "deviceId",
        "displayName",
        "operatingSystem",
        "operatingSystemVersion",
        "approximateLastSignInDateTime"
    ];

    private readonly GraphServiceClient _graphClient;
    private readonly ILogger<GraphEntraIdDeviceClient> _logger;

    public GraphEntraIdDeviceClient(GraphServiceClient graphClient, ILogger<GraphEntraIdDeviceClient> logger)
    {
        _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<DeviceRecord>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var devices = new List<DeviceRecord>();

        var response = await _graphClient.Devices.GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Top = 999;
            requestConfiguration.QueryParameters.Select = SelectFields;
        }, cancellationToken).ConfigureAwait(false);

        while (response is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (response.Value is not null)
            {
                foreach (var device in response.Value)
                {
                    if (!Guid.TryParse(device.Id, out var deviceId))
                    {
                        continue;
                    }

                    Guid? azureDeviceId = null;
                    if (!string.IsNullOrWhiteSpace(device.DeviceId) && Guid.TryParse(device.DeviceId, out var parsedDeviceId))
                    {
                        azureDeviceId = parsedDeviceId;
                    }

                    var record = new DeviceRecord(
                        deviceId,
                        device.DisplayName ?? string.Empty,
                        azureDeviceId,
                        device.OperatingSystem,
                        device.OperatingSystemVersion,
                        device.ApproximateLastSignInDateTime,
                        "EntraID");

                    devices.Add(record);
                }
            }

            if (string.IsNullOrEmpty(response.OdataNextLink))
            {
                break;
            }

            response = await _graphClient.Devices.WithUrl(response.OdataNextLink).GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Fetched {Count} devices from Entra ID", devices.Count);
        }

        return devices;
    }

    public async Task DeleteDeviceAsync(Guid id, CancellationToken cancellationToken)
    {
        await _graphClient.Devices[id.ToString()].DeleteAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
        {
            _logger.LogInformation("Deleted Entra device {Id}", id);
        }
    }
}
