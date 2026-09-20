using System.Text.Json;
using System.Text.Json.Nodes;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class RequestSerializationTests
{
    [Fact]
    public void DocumentedExample_SerializesToTheDocumentedWireShape()
    {
        var request = SystemOneRequest.Create(
            "I was charged twice. Please help ASAP.",
            new Dictionary<string, TypeSafeQuestion>
            {
                ["billing"] = new Noul { Instructions = "Is this about billing?" },
                ["tone"] = new Choice
                {
                    Instructions = "What is the tone?",
                    Criteria = new Dictionary<string, TypeSafeContent?> { ["calm"] = null, ["angry"] = null }
                },
                ["urgency"] = new Score { Instructions = "How urgent is this?", Criteria = ["low", "medium", "high"] }
            },
            "jev-latest");

        const string expected = """
            {"state":"I was charged twice. Please help ASAP.","model":"jev-latest","questions":{"billing":{"type":"noul","instructions":"Is this about billing?"},"tone":{"type":"choice","instructions":"What is the tone?","criteria":{"calm":null,"angry":null}},"urgency":{"type":"score","instructions":"How urgent is this?","criteria":["low","medium","high"]}}}
            """;
        var actual = JsonSerializer.Serialize(request, TypeSafeJsonContext.Default.SystemOneRequest);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), $"Actual wire JSON: {actual}");
    }

    [Fact]
    public void Question_DiscriminatorIsWrittenFirst()
    {
        TypeSafeQuestion question = new Noul { Instructions = "Is this about billing?" };

        var wire = JsonSerializer.Serialize(question, TypeSafeJsonContext.Default.TypeSafeQuestion);

        Assert.Equal("""{"type":"noul","instructions":"Is this about billing?"}""", wire);
    }

    [Fact]
    public void OptionalMembers_AreOmittedWhenNull()
    {
        TypeSafeQuestion question = new Noul();

        var wire = JsonSerializer.Serialize(question, TypeSafeJsonContext.Default.TypeSafeQuestion);

        Assert.Equal("""{"type":"noul"}""", wire);
    }

    [Fact]
    public void NoulCriteria_UsesTrueAndFalseNames()
    {
        TypeSafeQuestion question = new Noul
        {
            Instructions = "Is the customer asking for money back?",
            Criteria = new NoulCriteria { WhenTrue = "A refund is requested.", WhenFalse = "No refund mentioned." }
        };

        var wire = JsonSerializer.Serialize(question, TypeSafeJsonContext.Default.TypeSafeQuestion);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                {"type":"noul","instructions":"Is the customer asking for money back?","criteria":{"true":"A refund is requested.","false":"No refund mentioned."}}
                """),
            JsonNode.Parse(wire)), $"Actual wire JSON: {wire}");
    }

    [Fact]
    public void ObjectState_IsSentAsAnObject()
    {
        var request = SystemOneRequest.Create(
            new JsonObject { ["ticket"] = new JsonObject { ["messages"] = new JsonArray("Charged twice!") } },
            new Dictionary<string, TypeSafeQuestion> { ["is_urgent"] = new Noul() },
            "jev-latest");

        var wire = JsonSerializer.Serialize(request, TypeSafeJsonContext.Default.SystemOneRequest);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                {"state":{"ticket":{"messages":["Charged twice!"]}},"model":"jev-latest","questions":{"is_urgent":{"type":"noul"}}}
                """),
            JsonNode.Parse(wire)), $"Actual wire JSON: {wire}");
    }
}
