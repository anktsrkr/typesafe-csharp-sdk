using System.Text.Json;
using System.Text.Json.Nodes;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class TypeSafeContentTests
{
    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("{\"document\":\"t-1\"}", "{\"document\":\"t-1\"}")]
    [InlineData("[1,\"two\"]", "[1,\"two\"]")]
    public void Content_SerializesAsRawJson(string wire, string input)
    {
        TypeSafeContent content = input.StartsWith('[') || input.StartsWith('{')
            ? TypeSafeContent.FromJson(JsonNode.Parse(input)!)
            : TypeSafeContent.FromString(input);

        Assert.Equal(wire, JsonSerializer.Serialize(content, TypeSafeJsonContext.Default.TypeSafeContent));
    }

    [Fact]
    public void ImplicitString_ProducesTextContent()
    {
        TypeSafeContent content = "Is this about billing?";

        Assert.Equal("\"Is this about billing?\"", JsonSerializer.Serialize(content, TypeSafeJsonContext.Default.TypeSafeContent));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    public void Converter_RejectsInvalidRoots(string wire) =>
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(wire, TypeSafeJsonContext.Default.TypeSafeContent));

    [Fact]
    public void FromJson_RejectsNumberAndBooleanAndNullRoots()
    {
        Assert.Throws<ArgumentException>(() => TypeSafeContent.FromJson(JsonValue.Create(42)!));
        Assert.Throws<ArgumentException>(() => TypeSafeContent.FromJson(JsonValue.Create(true)!));
        Assert.Throws<ArgumentNullException>(() => TypeSafeContent.FromJson(null!));
    }

    [Fact]
    public void Content_DeepClonesTheCallerNode()
    {
        var node = new JsonObject { ["ticket"] = "t-1" };

        TypeSafeContent content = node;
        node["ticket"] = "t-2";

        Assert.Equal("""{"ticket":"t-1"}""", JsonSerializer.Serialize(content, TypeSafeJsonContext.Default.TypeSafeContent));
    }

    [Fact]
    public void ToJsonNode_ReturnsAnIndependentClone()
    {
        TypeSafeContent content = new JsonObject { ["ticket"] = "t-1" };

        var clone = content.ToJsonNode();
        clone["ticket"] = "t-2";

        Assert.Equal("""{"ticket":"t-1"}""", JsonSerializer.Serialize(content, TypeSafeJsonContext.Default.TypeSafeContent));
    }
}
