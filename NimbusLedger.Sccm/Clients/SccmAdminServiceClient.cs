using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NimbusLedger.Sccm.Abstractions;
using NimbusLedger.Sccm.Models;
using NimbusLedger.Sccm.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NimbusLedger.Sccm.Clients;

public sealed class SccmAdminServiceClient : ISccmAdminServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<SccmAdminServiceClient> _logger;
    private readonly JsonSerializerOptions _json;

    public SccmAdminServiceClient(HttpClient http, IOptions<SccmCleanupOptions> opts, ILogger<SccmAdminServiceClient> logger)
    {
        _http = http;
        _logger = logger;
        _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var baseUrl = opts.Value.AdminServiceBaseUrl?.TrimEnd('/') ?? throw new InvalidOperationException("SCCM AdminService base URL not configured");
        _http.BaseAddress = new Uri(baseUrl + "/AdminService/v1.0/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async IAsyncEnumerable<CmDevice> GetDevicesAsync(string? odataFilter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string path = "Devices" + (string.IsNullOrWhiteSpace(odataFilter) ? string.Empty : $"?$filter={Uri.EscapeDataString(odataFilter)}");
        using var resp = await _http.GetAsync(path, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Unauthorized to SCCM AdminService");
        resp.EnsureSuccessStatusCode();

        await foreach (var device in JsonSerializer.DeserializeAsyncEnumerable<CmDevice>(await resp.Content.ReadAsStreamAsync(ct), _json, ct))
        {
            if (device != null) yield return device;
        }
    }

    public async Task<bool> DeleteDeviceByResourceIdAsync(int resourceId, CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync($"wmi/Devices({resourceId})", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }
}
