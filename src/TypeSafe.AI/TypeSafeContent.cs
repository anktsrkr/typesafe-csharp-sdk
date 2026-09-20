using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TypeSafe.AI;

/// <summary>
/// Text, a JSON object, or a JSON array — the documented content shape for state, instructions,
/// and criteria descriptions. Numbers, booleans, and null are not valid content roots.
/// </summary>
/// <remarks>Instances are immutable: the wrapped JSON is deep-cloned at construction, so callers
/// may reuse or mutate their own nodes after conversion.</remarks>
[JsonConverter(typeof(TypeSafeContentJsonConverter))]
public sealed class TypeSafeContent
{
    private readonly JsonNode _root;

    private TypeSafeContent(JsonNode root) => _root = root;

    /// <summary>Creates content from plain text.</summary>
    public static TypeSafeContent FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TypeSafeContent(JsonValue.Create(text)!);
    }

    /// <summary>Creates content from a JSON node; the node is validated and deep-cloned.</summary>
    public static TypeSafeContent FromJson(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var kind = node.GetValueKind();
        if (kind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
            throw new ArgumentException($"TypeSafe content must be a string, object, or array, but was {kind}.", nameof(node));
        return new TypeSafeContent(node.DeepClone());
    }

    public static implicit operator TypeSafeContent(string text) => FromString(text);

    public static implicit operator TypeSafeContent(JsonNode node) => FromJson(node);

    /// <summary>Creates content from a strongly-typed value serialized through the caller's
    /// source-generated metadata. Anonymous objects are deliberately unsupported: serializing
    /// them requires runtime reflection.</summary>
    public static TypeSafeContent FromObject<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var node = JsonSerializer.SerializeToNode(value, typeInfo)
            ?? throw new ArgumentException("The value serialized to a null JSON root; TypeSafe content must be a string, object, or array.", nameof(value));
        return FromJson(node);
    }

    /// <summary>Returns an independent clone of the wrapped JSON.</summary>
    public JsonNode ToJsonNode() => _root.DeepClone();

    internal void WriteTo(Utf8JsonWriter writer) => _root.WriteTo(writer);
}

/// <summary>Serializes <see cref="TypeSafeContent"/> as raw JSON and rejects invalid roots when reading.</summary>
public sealed class TypeSafeContentJsonConverter : JsonConverter<TypeSafeContent>
{
    public override TypeSafeContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array
                => TypeSafeContent.FromJson(JsonNode.Parse(document.RootElement.GetRawText())!),
            _ => throw new JsonException($"TypeSafe content must be a string, object, or array, but was {document.RootElement.ValueKind}.")
        };
    }

    public override void Write(Utf8JsonWriter writer, TypeSafeContent value, JsonSerializerOptions options)
    {
        // Null is meaningful on the wire: choice criteria map undescribed labels to explicit nulls.
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        value.WriteTo(writer);
    }
}
