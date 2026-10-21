namespace NimbusLedger.Core.Models;

public sealed record CleanupAction(string Source, Guid DeviceId, Guid AzureAdDeviceId);

public sealed record CleanupPlan(
    DateTimeOffset CreatedAt,
    IReadOnlyList<CleanupAction> Actions,
    IReadOnlyList<string> Blockers);
