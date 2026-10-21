namespace NimbusLedger.Core.Models;

public sealed record ConfigMgrDevice(
    int ResourceId,
    string Name,
    DateTimeOffset? LastActiveTime,
    bool IsObsolete,
    int? ClientActiveStatus)
{
    public Guid? ActiveDirectoryObjectGuid { get; init; }
}
