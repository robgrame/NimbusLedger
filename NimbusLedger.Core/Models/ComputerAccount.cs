namespace NimbusLedger.Core.Models;

/// <summary>
/// Represents a Windows computer account discovered in on-premises Active Directory.
/// </summary>
/// <param name="ObjectGuid">The object GUID of the computer account.</param>
/// <param name="SamAccountName">The SAM account name (sAMAccountName).</param>
/// <param name="DistinguishedName">The distinguished name of the computer account.</param>
/// <param name="DnsHostName">The optional DNS host name.</param>
/// <param name="OperatingSystem">The reported operating system caption.</param>
/// <param name="OperatingSystemVersion">The reported operating system version.</param>
/// <param name="LastLogonTimestamp">The last logon timestamp converted to UTC.</param>
/// <param name="WhenChanged">The time the object was last changed.</param>
/// <param name="AzureAdDeviceId">The Azure AD device identifier stored on the object when hybrid joined.</param>
public sealed record ComputerAccount(
    Guid ObjectGuid,
    string SamAccountName,
    string DistinguishedName,
    string? DnsHostName,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    DateTimeOffset? LastLogonTimestamp,
    DateTimeOffset? WhenChanged,
    Guid? AzureAdDeviceId);
