using System.Text.Json.Serialization;

namespace WhoCarried.Core;

/// <summary>Compile-time JSON serializer (no reflection needed at runtime).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(RunStats))]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(CardGenerationEvent))]
internal partial class WhoCarriedJson : JsonSerializerContext
{
}
