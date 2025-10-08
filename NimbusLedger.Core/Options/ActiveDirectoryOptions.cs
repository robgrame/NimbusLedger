namespace NimbusLedger.Core.Options;

/// <summary>
/// Configuration settings for the on-premises Active Directory query.
/// </summary>
public sealed class ActiveDirectoryOptions
{
    /// <summary>
    /// The host name or IP address of the LDAP endpoint (domain controller or load balancer).
    /// </summary>
    public string LdapServer { get; set; } = string.Empty;

    /// <summary>
    /// The LDAP port to connect to. Use 636 for LDAPS.
    /// </summary>
    public int Port { get; set; } = 636;

    /// <summary>
    /// Indicates whether to negotiate TLS/SSL for the LDAP connection.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// Controls whether to bypass certificate validation. Only use for diagnostics.
    /// </summary>
    public bool AllowInvalidCertificates { get; set; }
        = false;

    /// <summary>
    /// The distinguished name that acts as the query root, e.g. "DC=contoso,DC=com".
    /// </summary>
    public string BaseDn { get; set; } = string.Empty;

    /// <summary>
    /// Optional service account user in UPN format.
    /// </summary>
    public string? Username { get; set; }
        = null;

    /// <summary>
    /// Optional service account password. Prefer storing this in a secret store.
    /// </summary>
    public string? Password { get; set; }
        = null;

    /// <summary>
    /// LDAP filter applied to the query. Defaults to the computer object filter.
    /// </summary>
    public string Filter { get; set; } = "(&(objectCategory=computer)(objectClass=computer))";

    /// <summary>
    /// Page size to use for paged LDAP queries.
    /// </summary>
    public int PageSize { get; set; } = 500;

    /// <summary>
    /// Maximum age in days for last logon to consider the device as active.
    /// </summary>
    public int ActivityWindowDays { get; set; } = 30;

    /// <summary>
    /// Additional attributes to retrieve from LDAP besides the defaults.
    /// </summary>
    public string[] AdditionalAttributes { get; set; } = Array.Empty<string>();
}
