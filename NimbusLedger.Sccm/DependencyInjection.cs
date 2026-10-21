using NimbusLedger.Sccm.Abstractions;
using NimbusLedger.Sccm.Clients;
using NimbusLedger.Sccm.Options;
using NimbusLedger.Sccm.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NimbusLedger.Core.Abstractions;
using System.Net;

namespace NimbusLedger.Sccm;

public static class DependencyInjection
{
    public static IServiceCollection AddSccmCleanup(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<SccmCleanupOptions>()
            .Bind(config.GetSection("HybridLedger:Sccm"))
            .Validate(o => !o.Enabled || Uri.TryCreate(o.AdminServiceBaseUrl, UriKind.Absolute, out Uri? uri)
                && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo),
                "Enabled ConfigMgr inventory requires an HTTPS AdminService URL.")
            .Validate(o => o.InactiveDaysThreshold > 0 && o.ObsoleteDaysThreshold > 0
                && o.MaxDeletesPerRun > 0 && o.AllowedResourceIds.All(id => id > 0),
                "ConfigMgr safety limits and resource IDs must be positive.")
            .ValidateOnStart();
        services.AddHttpClient<ISccmAdminServiceClient, SccmAdminServiceClient>(http =>
            http.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                SccmCleanupOptions options = provider.GetRequiredService<IOptions<SccmCleanupOptions>>().Value;
                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    Credentials = string.IsNullOrWhiteSpace(options.DomainUsername)
                        ? CredentialCache.DefaultNetworkCredentials
                        : new NetworkCredential(options.DomainUsername, options.DomainPassword, options.Domain)
                };
            });
        services.AddSingleton<IConfigMgrDeviceClient>(provider =>
            new OptionalConfigMgrClient(provider, provider.GetRequiredService<IOptions<SccmCleanupOptions>>()));
        services.AddSingleton<ISccmCleanupService, SccmCleanupService>();
        return services;
    }

    private sealed class OptionalConfigMgrClient(IServiceProvider provider, IOptions<SccmCleanupOptions> options) : IConfigMgrDeviceClient
    {
        public bool Enabled => options.Value.Enabled;

        public Task<IReadOnlyList<Core.Models.ConfigMgrDevice>> GetDevicesAsync(CancellationToken cancellationToken) =>
            new ConfigMgrDeviceClient(provider.GetRequiredService<ISccmAdminServiceClient>(), options)
                .GetDevicesAsync(cancellationToken);
    }
}
