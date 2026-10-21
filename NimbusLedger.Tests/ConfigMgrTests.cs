using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NimbusLedger.Sccm.Clients;
using NimbusLedger.Sccm.Models;
using NimbusLedger.Sccm.Options;

namespace NimbusLedger.Tests;

public sealed class ConfigMgrTests
{
    [Fact]
    public async Task ReadsODataPagesAndJoinsAdObjectGuid()
    {
        string guid = Convert.ToBase64String(TestInventory.DeviceId.ToByteArray());
        List<string> paths = [];
        using HttpClient http = new(new Handler(request =>
        {
            string path = request.RequestUri!.PathAndQuery;
            paths.Add(path);
            if (path.Contains("SMS_R_System"))
                return $$"""{"value":[{"ResourceID":1,"ObjectGUID":"{{guid}}"},{"ResourceID":2,"ObjectGUID":null}]}""";
            return path.Contains("page=2")
                ? """{"value":[{"ResourceID":2,"Name":"DEVICE02","IsObsolete":false}]}"""
                : """{"value":[{"ResourceID":1,"Name":"DEVICE01","IsObsolete":true,"LastActiveTime":"2025-01-01T00:00:00Z"}],"@odata.nextLink":"https://provider.example.com/AdminService/v1.0/Device?page=2"}""";
        }));
        SccmAdminServiceClient client = Create(http);
        List<CmDevice> devices = [];
        await foreach (CmDevice device in client.GetDevicesAsync()) devices.Add(device);
        Assert.Equal(2, devices.Count);
        Assert.Equal(TestInventory.DeviceId, devices[0].ActiveDirectoryObjectGuid);
        Assert.Null(devices[1].ActiveDirectoryObjectGuid);
        Assert.Equal(3, paths.Count);
        Assert.All(paths, path => Assert.StartsWith("/AdminService/", path));
    }

    [Theory]
    [InlineData("https://untrusted.example.com/AdminService/v1.0/Device")]
    [InlineData("http://provider.example.com/AdminService/v1.0/Device")]
    [InlineData("https://provider.example.com/other/Device")]
    [InlineData("https://provider.example.com/AdminService/v1.0/Device")]
    public async Task RejectsForeignDowngradedEscapedAndRepeatedPagination(string nextLink)
    {
        int requests = 0;
        using HttpClient http = new(new Handler(request =>
        {
            requests++;
            if (request.RequestUri!.AbsolutePath.Contains("SMS_R_System")) return """{"value":[]}""";
            return $$"""{"value":[],"@odata.nextLink":"{{nextLink}}"}""";
        }));
        SccmAdminServiceClient client = Create(http);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (CmDevice _ in client.GetDevicesAsync()) { }
        });
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task RejectsMalformedEnvelopeInsteadOfTreatingItAsEmptyInventory()
    {
        using HttpClient http = new(new Handler(_ => "[]"));
        SccmAdminServiceClient client = Create(http);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(async () =>
        {
            await foreach (CmDevice _ in client.GetDevicesAsync()) { }
        });
    }

    [Fact]
    public async Task DeleteUsesResourceClassNotInventedDevicesRoute()
    {
        string? path = null;
        using HttpClient http = new(new Handler(request =>
        {
            path = request.RequestUri!.AbsolutePath;
            Assert.Equal(HttpMethod.Delete, request.Method);
            return "{}";
        }));
        Assert.True(await Create(http).DeleteDeviceByResourceIdAsync(1));
        Assert.Equal("/AdminService/wmi/SMS_R_System(1)", path);
    }

    private static SccmAdminServiceClient Create(HttpClient http) => new(http,
        Options.Create(new SccmCleanupOptions { AdminServiceBaseUrl = "https://provider.example.com/AdminService" }),
        NullLogger<SccmAdminServiceClient>.Instance);

    private sealed class Handler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") });
    }
}
