using Azure.Identity;
using Azure.Core; // Added for TokenCredential
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
            GraphOptions graph = options.Graph;

            TokenCredential credential;
            if (!string.IsNullOrWhiteSpace(graph.TenantId) &&
                !string.IsNullOrWhiteSpace(graph.ClientId) &&
                !string.IsNullOrWhiteSpace(graph.ClientSecret))
            {
                credential = new ClientSecretCredential(graph.TenantId, graph.ClientId, graph.ClientSecret);
            }
            else
            {
                var credentialOptions = new DefaultAzureCredentialOptions
                {
                    VisualStudioTenantId = graph.TenantId,
                    SharedTokenCacheTenantId = graph.TenantId
                };

                if (!string.IsNullOrWhiteSpace(graph.ClientId))
                {
                    credentialOptions.ManagedIdentityClientId = graph.ClientId;
                }

                if (!string.IsNullOrWhiteSpace(graph.TenantId))
                {
                    credentialOptions.TenantId = graph.TenantId;
                }

                credential = new DefaultAzureCredential(credentialOptions);
            }

            var client = new GraphServiceClient(credential, graph.Scopes)
            {
                // Configure request timeout if needed via middleware (placeholder; Graph SDK v5 uses HttpClientFactory internally soon)
            };
            return client;
        });

        return services;
    }
}
