using Azure.Identity;
using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Options;
using NimbusLedger.Infrastructure.ActiveDirectory;
using NimbusLedger.Infrastructure.Graph;
using NimbusLedger.Infrastructure.Services;
using NimbusLedger.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

namespace NimbusLedger.Infrastructure;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddHybridLedgerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HybridLedgerOptions>(configuration.GetSection("HybridLedger"));

        services.AddSingleton<IActiveDirectoryClient, LdapActiveDirectoryClient>();
        services.AddSingleton<IEntraIdDeviceClient, GraphEntraIdDeviceClient>();
        services.AddSingleton<IIntuneDeviceClient, GraphIntuneDeviceClient>();
        services.AddSingleton<ILedgerSnapshotStore, FileLedgerSnapshotStore>();
        services.AddSingleton<ILedgerService, LedgerReconciliationService>();
        services.AddSingleton<ICleanupService, CleanupService>();

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<HybridLedgerOptions>>().Value;
            var credentialOptions = new DefaultAzureCredentialOptions
            {
                VisualStudioTenantId = options.Graph.TenantId,
                SharedTokenCacheTenantId = options.Graph.TenantId
            };

            if (!string.IsNullOrWhiteSpace(options.Graph.ClientId))
            {
                credentialOptions.ManagedIdentityClientId = options.Graph.ClientId;
            }

            if (!string.IsNullOrWhiteSpace(options.Graph.TenantId))
            {
                credentialOptions.TenantId = options.Graph.TenantId;
            }

            var credential = new DefaultAzureCredential(credentialOptions);

            return new GraphServiceClient(credential, options.Graph.Scopes);
        });

        return services;
    }
}
