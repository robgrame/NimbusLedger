namespace NimbusLedger.Core.Models;

/// <summary>
/// Aggregated snapshot of the current hybrid join posture.
/// </summary>
public sealed record LedgerSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<ComputerAccount> ActiveDirectoryComputers,
    IReadOnlyList<DeviceRecord> EntraDevices,
    IReadOnlyList<DeviceRecord> IntuneDevices,
    IReadOnlyList<ComputerAccount> MissingInEntra,
    IReadOnlyList<ComputerAccount> MissingInIntune,
    LedgerSnapshotMetrics Metrics);

/// <summary>
/// Pre-computed counters to simplify dashboard rendering.
/// </summary>
/// <param name="ActiveDirectoryCount">Total number of AD computer accounts considered in scope.</param>
/// <param name="EntraCount">Total number of Entra ID devices matching the AD scope.</param>
/// <param name="IntuneCount">Total number of Intune devices matching the AD scope.</param>
/// <param name="MissingInEntraCount">Number of AD devices missing from Entra ID.</param>
/// <param name="MissingInIntuneCount">Number of AD devices missing from Intune.</param>
/// <param name="StaleDevicesCount">Number of AD devices identified as stale based on activity window.</param>
public sealed record LedgerSnapshotMetrics(
    int ActiveDirectoryCount,
    int EntraCount,
    int IntuneCount,
    int MissingInEntraCount,
    int MissingInIntuneCount,
    int StaleDevicesCount);
