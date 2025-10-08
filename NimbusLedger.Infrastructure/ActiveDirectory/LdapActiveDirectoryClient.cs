using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using NimbusLedger.Core.Abstractions;
using NimbusLedger.Core.Models;
using NimbusLedger.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NimbusLedger.Infrastructure.ActiveDirectory;

public sealed class LdapActiveDirectoryClient : IActiveDirectoryClient
{
    private static readonly string[] DefaultAttributes =
    [
        "objectGUID",
        "sAMAccountName",
        "distinguishedName",
        "dNSHostName",
        "operatingSystem",
        "operatingSystemVersion",
        "lastLogonTimestamp",
        "whenChanged",
        "msDS-DeviceId"
    ];

    private readonly ActiveDirectoryOptions _options;
    private readonly ILogger<LdapActiveDirectoryClient> _logger;

    public LdapActiveDirectoryClient(IOptions<HybridLedgerOptions> options, ILogger<LdapActiveDirectoryClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value.ActiveDirectory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ComputerAccount>> GetComputersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() => QueryInternal(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<ComputerAccount> QueryInternal(CancellationToken cancellationToken)
    {
        var attributes = DefaultAttributes
            .Concat(_options.AdditionalAttributes ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new List<ComputerAccount>();

        using var connection = CreateConnection();

        var searchRequest = new SearchRequest(
            _options.BaseDn,
            _options.Filter,
            SearchScope.Subtree,
            attributes);

        var pageControl = new PageResultRequestControl(_options.PageSize);
        searchRequest.Controls.Add(pageControl);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Querying LDAP server {Server}:{Port} with page size {PageSize}", _options.LdapServer, _options.Port, _options.PageSize);
            }

            var response = (SearchResponse)connection.SendRequest(searchRequest);

            foreach (SearchResultEntry entry in response.Entries)
            {
                var account = MapEntry(entry);
                if (account is null)
                {
                    continue;
                }

                results.Add(account);
            }

            var pageResponse = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
            if (pageResponse is null || pageResponse.Cookie.Length == 0)
            {
                break;
            }

            pageControl.Cookie = pageResponse.Cookie;
        }

        return results;
    }

    private LdapConnection CreateConnection()
    {
        var identifier = new LdapDirectoryIdentifier(_options.LdapServer, _options.Port, false, false);
        LdapConnection connection;

        if (!string.IsNullOrWhiteSpace(_options.Username) && !string.IsNullOrWhiteSpace(_options.Password))
        {
            connection = new LdapConnection(identifier, new NetworkCredential(_options.Username, _options.Password));
        }
        else
        {
            connection = new LdapConnection(identifier);
        }

        connection.AuthType = AuthType.Negotiate;
        connection.SessionOptions.ProtocolVersion = 3;

        if (_options.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }

        if (_options.AllowInvalidCertificates)
        {
            connection.SessionOptions.VerifyServerCertificate += (_, _) => true;
        }

        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        connection.Timeout = TimeSpan.FromSeconds(30);

        return connection;
    }

    private ComputerAccount? MapEntry(System.DirectoryServices.Protocols.SearchResultEntry entry)
    {
        if (!entry.Attributes.Contains("objectGUID"))
        {
            return null;
        }

        try
        {
            var guidBytes = (byte[])entry.Attributes["objectGUID"].GetValues(typeof(byte[]))[0]!;
            var objectGuid = new Guid(guidBytes);

            var samAccountName = entry.Attributes["sAMAccountName"]?.GetValues(typeof(string)).Cast<string?>().FirstOrDefault();
            var distinguishedName = entry.Attributes["distinguishedName"]?.GetValues(typeof(string)).Cast<string?>().FirstOrDefault();

            if (string.IsNullOrWhiteSpace(samAccountName) || string.IsNullOrWhiteSpace(distinguishedName))
            {
                return null;
            }

            var dnsName = entry.Attributes["dNSHostName"]?.GetValues(typeof(string)).Cast<string?>().FirstOrDefault();
            var operatingSystem = entry.Attributes["operatingSystem"]?.GetValues(typeof(string)).Cast<string?>().FirstOrDefault();
            var osVersion = entry.Attributes["operatingSystemVersion"]?.GetValues(typeof(string)).Cast<string?>().FirstOrDefault();

            DateTimeOffset? lastLogon = null;
            if (entry.Attributes.Contains("lastLogonTimestamp"))
            {
                var lastLogonValue = entry.Attributes["lastLogonTimestamp"].GetValues(typeof(string)).Cast<string?>().FirstOrDefault();
                if (long.TryParse(lastLogonValue, out var fileTime))
                {
                    lastLogon = DateTimeOffset.FromFileTime(fileTime);
                }
            }

            DateTimeOffset? whenChanged = null;
            if (entry.Attributes.Contains("whenChanged"))
            {
                var whenChangedValue = entry.Attributes["whenChanged"].GetValues(typeof(string)).Cast<string?>().FirstOrDefault();
                if (DateTimeOffset.TryParse(whenChangedValue, out var parsed))
                {
                    whenChanged = parsed;
                }
            }

            Guid? azureAdDeviceId = null;
            if (entry.Attributes.Contains("msDS-DeviceId"))
            {
                var deviceId = entry.Attributes["msDS-DeviceId"].GetValues(typeof(string)).Cast<string?>().FirstOrDefault();
                if (Guid.TryParse(deviceId, out var parsedDeviceId))
                {
                    azureAdDeviceId = parsedDeviceId;
                }
            }

            return new ComputerAccount(
                objectGuid,
                samAccountName!,
                distinguishedName!,
                dnsName,
                operatingSystem,
                osVersion,
                lastLogon,
                whenChanged,
                azureAdDeviceId);
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(ex, "Failed to map LDAP entry {DistinguishedName}", entry.DistinguishedName);
            }
            return null;
        }
    }
}
