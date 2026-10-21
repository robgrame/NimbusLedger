using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NimbusLedger.Core.Abstractions;

namespace NimbusLedger.Tests;

public sealed class WebAccessTests
{
    [Theory]
    [InlineData("/", null, HttpStatusCode.Unauthorized)]
    [InlineData("/api/snapshot", null, HttpStatusCode.Unauthorized)]
    [InlineData("/api/snapshot", "outsider", HttpStatusCode.Forbidden)]
    [InlineData("/api/snapshot", "reader", HttpStatusCode.OK)]
    [InlineData("/", "reader", HttpStatusCode.OK)]
    [InlineData("/health/live", null, HttpStatusCode.OK)]
    [InlineData("/health/ready", null, HttpStatusCode.OK)]
    public async Task InventoryEndpointsRequireAuthenticatedAuthorizedGroup(string path, string? identity, HttpStatusCode expected)
    {
        await using Factory factory = new();
        using HttpClient http = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (identity is not null) http.DefaultRequestHeaders.Add("X-Test-Identity", identity);
        using HttpResponseMessage response = await http.GetAsync(path);
        Assert.True(expected == response.StatusCode,
            $"Expected {expected}, received {response.StatusCode}. {await response.Content.ReadAsStringAsync()}");
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["WebAccess:AllowedGroups:0"] = "LedgerReaders" }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILedgerSnapshotStore>(new MemorySnapshotStore { Snapshot = TestInventory.Snapshot() });
                services.AddSingleton<IAuthenticationSchemeProvider, TestSchemeProvider>();
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
            });
        }
    }

    private sealed class TestSchemeProvider(IOptions<AuthenticationOptions> options) : AuthenticationSchemeProvider(options)
    {
        // TestServer has no connection persistence feature; real Negotiate is exercised only on Kestrel/IIS.
        public override Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync() =>
            Task.FromResult(Enumerable.Empty<AuthenticationScheme>());
    }

    private sealed class TestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string value = Request.Headers["X-Test-Identity"].ToString();
            if (value.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            List<Claim> claims = [new(ClaimTypes.Name, "test-user")];
            if (value == "reader") claims.Add(new(ClaimTypes.Role, "LedgerReaders"));
            ClaimsPrincipal principal = new(new ClaimsIdentity(claims, "Test"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }
}
