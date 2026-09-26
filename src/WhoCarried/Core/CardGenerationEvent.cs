using System.Text.Json.Serialization;

namespace WhoCarried.Core;

/// <summary>One generated output, with its contributor and recipient; replay counts its gift subset once.</summary>
public sealed class CardGenerationEvent
{
    [JsonPropertyName("v")] public int Version { get; set; }
    [JsonPropertyName("contributor")] public ulong Contributor { get; set; }
    [JsonPropertyName("recipient")] public ulong? Recipient { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("effect")] public string? Effect { get; set; }
}
