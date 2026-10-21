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
        services.AddOptions<HybridLedgerOptions>()
            .Bind(configuration.GetSection("HybridLedger"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ActiveDirectory.LdapServer)
                && !string.IsNullOrWhiteSpace(o.ActiveDirectory.BaseDn)
                && o.ActiveDirectory.PageSize > 0 && o.ActiveDirectory.ActivityWindowDays >= 14,
                "LDAP server, base DN, page size and an activity window of at least 14 days are required.")
            .Validate(o => !o.ActiveDirectory.AllowInvalidCertificates && o.ActiveDirectory.UseSsl,
                "LDAPS with certificate validation is required.")
            .Validate(o => o.Cleanup.MaxDeletesPerRun > 0 && o.Cleanup.MaxDeletePercentage is > 0 and <= 100
                && o.Cleanup.MinimumAdDevices > 0 && o.Cleanup.MinimumEntraDevices > 0
                && o.Cleanup.MinimumIntuneDevices > 0 && o.Cleanup.IntuneFreshWindowDays >= 14
                && o.Cleanup.MaxSnapshotAge > TimeSpan.Zero && o.Cleanup.MaxCollectionDuration > TimeSpan.Zero
                && o.Cleanup.AllowedAzureAdDeviceIds.All(id => id != Guid.Empty),
                "Cleanup safety limits must be positive and device IDs must be valid.")
            .Validate(o => o.Snapshot.HistorySize > 0 && o.Snapshot.MaxAge > TimeSpan.Zero
                && !string.IsNullOrWhiteSpace(o.Snapshot.RootPath)
                && !string.IsNullOrWhiteSpace(o.Snapshot.LatestFileName)
                && Path.GetFileName(o.Snapshot.LatestFileName) == o.Snapshot.LatestFileName,
                "Snapshot storage settings are invalid.")
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICleanupPlanner, CleanupPlanner>();
        services.AddSingleton<ICleanupAuditLog, FileCleanupAuditLog>();

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
