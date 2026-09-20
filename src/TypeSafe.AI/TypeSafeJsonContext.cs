using System.Text.Json.Serialization;

namespace TypeSafe.AI;

/// <summary>Source-generated JSON for the supported wire graph; reflection-based serialization is
/// not used anywhere in the core. Optional members use per-property null-ignore so choice criteria
/// can carry explicit null values for undescribed labels. The response wire record keeps answers as
/// raw elements for the decoder's unknown-kind tolerance; semantic validation runs after decoding.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(SystemOneRequest))]
[JsonSerializable(typeof(SystemOneResponseWire))]
[JsonSerializable(typeof(TypeSafeQuestion))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, TypeSafeQuestion>))]
[JsonSerializable(typeof(TypeSafeAnswer))]
[JsonSerializable(typeof(TypeSafeContent))]
[JsonSerializable(typeof(TypeSafeUsage))]
public partial class TypeSafeJsonContext : JsonSerializerContext;
