using System.Text.Json.Serialization;

namespace NodLight.Core;

// Source-generated so the Native-AOT hook helper never needs reflection serialization.
// Enums are written as strings (readable on the wire, stable if members are reordered).
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(HookEvent))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
