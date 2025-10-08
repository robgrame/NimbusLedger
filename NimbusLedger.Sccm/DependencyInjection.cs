using NimbusLedger.Sccm.Abstractions;
using NimbusLedger.Sccm.Clients;
using NimbusLedger.Sccm.Options;
using NimbusLedger.Sccm.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NimbusLedger.Sccm;

public static class DependencyInjection
{
    public static IServiceCollection AddSccmCleanup(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SccmCleanupOptions>(config.GetSection("HybridLedger:Sccm"));

        services.AddHttpClient<SccmAdminServiceClient>();
        services.AddSingleton<ISccmAdminServiceClient, SccmAdminServiceClient>();
        services.AddSingleton<ISccmCleanupService, SccmCleanupService>();
        return services;
    }
}
