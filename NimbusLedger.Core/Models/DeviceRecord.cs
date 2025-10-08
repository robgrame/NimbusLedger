namespace NimbusLedger.Core.Models;

/// <summary>
/// Represents a device discovered from a cloud inventory (Entra ID or Intune).
/// </summary>
/// <param name="Id">The unique identifier for the device.</param>
/// <param name="DisplayName">The friendly device name.</param>
/// <param name="AzureAdDeviceId">The Azure AD device identifier if available.</param>
/// <param name="OperatingSystem">The reported operating system.</param>
/// <param name="OperatingSystemVersion">The reported operating system version.</param>
/// <param name="LastSyncDateTime">The last time the device successfully synced.</param>
/// <param name="Source">The inventory source (EntraID or Intune).</param>
public sealed record DeviceRecord(
    Guid Id,
    string DisplayName,
    Guid? AzureAdDeviceId,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    DateTimeOffset? LastSyncDateTime,
    string Source);
