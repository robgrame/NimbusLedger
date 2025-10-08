using System.Text.Json.Serialization;

namespace NimbusLedger.Sccm.Models;

public sealed class CmDevice
{
    [JsonPropertyName("ResourceId")] public int ResourceId { get; set; }
    [JsonPropertyName("Name")] public string? Name { get; set; }
    [JsonPropertyName("ClientActiveStatus")] public int ClientActiveStatus { get; set; }
    [JsonPropertyName("IsObsolete")] public int IsObsolete { get; set; }
    [JsonPropertyName("LastOnlineTime")] public string? LastOnlineTime { get; set; }
}
