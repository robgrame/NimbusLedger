using System.Text.Json.Serialization;

namespace NimbusLedger.Sccm.Models;

public sealed class CmDevice
{
    [JsonPropertyName("ResourceId")] public int ResourceId { get; set; }
    [JsonPropertyName("Name")] public string? Name { get; set; }
    [JsonPropertyName("ClientActiveStatus")] public int? ClientActiveStatus { get; set; }
    [JsonPropertyName("IsObsolete")] public bool IsObsolete { get; set; }
    [JsonPropertyName("LastActiveTime")] public DateTimeOffset? LastActiveTime { get; set; }
    [JsonIgnore] public Guid? ActiveDirectoryObjectGuid { get; set; }
}
