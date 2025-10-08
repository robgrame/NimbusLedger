namespace NimbusLedger.Core.Options;

/// <summary>
/// Configuration used to bootstrap the Microsoft Graph client.
/// </summary>
public sealed class GraphOptions
{
    /// <summary>
    /// The tenant identifier used for Microsoft Graph authentication.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Optional client identifier when using workload identities. Not required for managed identity.
    /// </summary>
    public string? ClientId { get; set; }
        = null;

    /// <summary>
    /// The scopes requested when acquiring Graph tokens. Defaults to .default.
    /// </summary>
    public string[] Scopes { get; set; } = ["https://graph.microsoft.com/.default"];

    /// <summary>
    /// Timeout applied to Graph requests.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
