using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NimbusLedger.Sccm.Abstractions;
using NimbusLedger.Sccm.Models;
using NimbusLedger.Sccm.Options;

namespace NimbusLedger.Sccm.Clients;

public sealed class SccmAdminServiceClient : ISccmAdminServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<SccmAdminServiceClient> _logger;
    private readonly Uri _baseUri;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public SccmAdminServiceClient(HttpClient http, IOptions<SccmCleanupOptions> options, ILogger<SccmAdminServiceClient> logger)
    {
        _http = http;
        _logger = logger;
        string address = options.Value.AdminServiceBaseUrl?.TrimEnd('/')
            ?? throw new InvalidOperationException("ConfigMgr AdminService base URL is required.");
        if (!address.EndsWith("/AdminService", StringComparison.OrdinalIgnoreCase))
            address += "/AdminService";
        _baseUri = new Uri(address + "/", UriKind.Absolute);
        if (_baseUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(_baseUri.UserInfo)
            || !string.IsNullOrEmpty(_baseUri.Query) || !string.IsNullOrEmpty(_baseUri.Fragment))
            throw new InvalidOperationException("ConfigMgr requires an HTTPS endpoint without embedded credentials, query or fragment.");
        _http.BaseAddress = _baseUri;
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async IAsyncEnumerable<CmDevice> GetDevicesAsync(string? odataFilter = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Dictionary<int, Guid?> identities = [];
        await foreach (DiscoveredResource resource in ReadPagesAsync<DiscoveredResource>("wmi/SMS_R_System?$select=ResourceID,ObjectGUID", ct).ConfigureAwait(false))
        {
            if (resource.ResourceId <= 0 || !identities.TryAdd(resource.ResourceId, ParseObjectGuid(resource.ObjectGuid)))
                throw new InvalidDataException("ConfigMgr discovery contains invalid or duplicate resource IDs.");
        }
        string query = "v1.0/Device" + (string.IsNullOrWhiteSpace(odataFilter)
            ? string.Empty : $"?$filter={Uri.EscapeDataString(odataFilter)}");
        await foreach (CmDevice device in ReadPagesAsync<CmDevice>(query, ct).ConfigureAwait(false))
        {
            if (device.ResourceId <= 0 || string.IsNullOrWhiteSpace(device.Name))
                throw new InvalidDataException("ConfigMgr inventory contains an invalid device.");
            device.ActiveDirectoryObjectGuid = identities.GetValueOrDefault(device.ResourceId);
            yield return device;
        }
    }

    private async IAsyncEnumerable<T> ReadPagesAsync<T>(string query, [EnumeratorCancellation] CancellationToken ct)
    {
        Uri? next = new(_baseUri, query);
        HashSet<string> visited = new(StringComparer.Ordinal);
        while (next is not null)
        {
            if (!SameEndpoint(next) || !visited.Add(next.AbsoluteUri))
                throw new InvalidDataException("ConfigMgr returned an untrusted or repeated pagination URL.");
            using HttpResponseMessage response = await _http.GetAsync(next, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("Unauthorized to ConfigMgr AdminService.");
            response.EnsureSuccessStatusCode();
            ODataPage<T>? page = await JsonSerializer.DeserializeAsync<ODataPage<T>>(
                await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), _json, ct).ConfigureAwait(false);
            if (page?.Value is null)
                throw new InvalidDataException("ConfigMgr inventory has no OData value collection.");
            foreach (T device in page.Value)
                yield return device;
            next = string.IsNullOrWhiteSpace(page.NextLink) ? null : new Uri(next, page.NextLink);
        }
    }

    public async Task<bool> DeleteDeviceByResourceIdAsync(int resourceId, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resourceId);
        using HttpResponseMessage response = await _http.DeleteAsync($"wmi/SMS_R_System({resourceId})", ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("ConfigMgr resource {ResourceId} no longer exists.", resourceId);
            return false;
        }
        response.EnsureSuccessStatusCode();
        return true;
    }

    private bool SameEndpoint(Uri uri) =>
        uri.Scheme == _baseUri.Scheme && uri.Host.Equals(_baseUri.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == _baseUri.Port && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath.StartsWith(_baseUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);

    private static Guid? ParseObjectGuid(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out Guid guid))
            return guid;
        byte[] bytes = element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray().Select(value => value.GetByte()).ToArray(),
            JsonValueKind.String => Convert.FromBase64String(element.GetString()!),
            _ => throw new InvalidDataException("Unsupported ConfigMgr AD object GUID encoding.")
        };
        if (bytes.Length != 16)
            throw new InvalidDataException("ConfigMgr AD object GUID must contain 16 bytes.");
        return new Guid(bytes);
    }

    private sealed class DiscoveredResource
    {
        [JsonPropertyName("ResourceID")] public int ResourceId { get; set; }
        [JsonPropertyName("ObjectGUID")] public JsonElement ObjectGuid { get; set; }
    }

    private sealed class ODataPage<T>
    {
        [JsonPropertyName("value")] public List<T>? Value { get; set; }
        [JsonPropertyName("@odata.nextLink")] public string? NextLink { get; set; }
    }
}
