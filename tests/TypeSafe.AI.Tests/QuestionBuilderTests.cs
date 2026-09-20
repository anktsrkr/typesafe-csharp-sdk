using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class QuestionBuilderTests
{
    [Fact]
    public void DocumentedExample_BuildProducesIdenticalWireJsonToRecordInitializers()
    {
        var built = SystemOneRequest.Create("I was charged twice. Please help ASAP.",
            Questions.Build(q => q
                .Noul("billing", "Is this about billing?")
                .Choice("tone", "What is the tone?", "calm", "angry")
                .Score("urgency", "How urgent is this?", "low", "medium", "high")),
            "jev-latest");

        var records = SystemOneRequest.Create("I was charged twice. Please help ASAP.",
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

        var builtWire = JsonSerializer.Serialize(built, TypeSafeJsonContext.Default.SystemOneRequest);
        var recordsWire = JsonSerializer.Serialize(records, TypeSafeJsonContext.Default.SystemOneRequest);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(recordsWire), JsonNode.Parse(builtWire)),
            $"Builder wire JSON: {builtWire}");
    }

    [Fact]
    public void Noul_NamedOutcomeDescriptions_MapToTrueAndFalseCriteria()
    {
        var questions = Questions.Build(q => q
            .Noul("refund", "Does the customer request a refund?",
                whenTrue: "Money back is explicitly requested",
                whenFalse: "No refund mentioned"));

        var wire = JsonSerializer.Serialize(questions["refund"], TypeSafeJsonContext.Default.TypeSafeQuestion);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                {"type":"noul","instructions":"Does the customer request a refund?","criteria":{"true":"Money back is explicitly requested","false":"No refund mentioned"}}
                """),
            JsonNode.Parse(wire)), $"Actual wire JSON: {wire}");
    }

    [Fact]
    public void Choice_DescribedOptionsBecomeStringsAndUndescribedBecomeExplicitNulls()
    {
        var questions = Questions.Build(q => q
            .Choice("priority", "What priority is this?", o => o
                .Option("low", "No time pressure")
                .Option("normal")
                .Option("high", "Blocks work right now")));

        var wire = JsonSerializer.Serialize(questions["priority"], TypeSafeJsonContext.Default.TypeSafeQuestion);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                {"type":"choice","instructions":"What priority is this?","criteria":{"low":"No time pressure","normal":null,"high":"Blocks work right now"}}
                """),
            JsonNode.Parse(wire)), $"Actual wire JSON: {wire}");
    }

    [Fact]
    public void Score_SubBuilderLevels_ConvertImplicitlyFromStrings()
    {
        var questions = Questions.Build(q => q
            .Score("severity", "How severe?", s => s
                .Level("minor")
                .Level("major")
                .Level("critical")));

        var wire = JsonSerializer.Serialize(questions["severity"], TypeSafeJsonContext.Default.TypeSafeQuestion);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                {"type":"score","instructions":"How severe?","criteria":["minor","major","critical"]}
                """),
            JsonNode.Parse(wire)), $"Actual wire JSON: {wire}");
    }

    [Fact]
    public void OmittedInstructions_AreAbsentFromTheWire()
    {
        var questions = Questions.Build(q => q
            .Noul("is_human")
            .Choice("team", o => o.Option("support").Option("engineering"))
            .Score("risk", s => s.Level("low").Level("high")));

        var noulWire = JsonSerializer.Serialize(questions["is_human"], TypeSafeJsonContext.Default.TypeSafeQuestion);
        var choiceWire = JsonSerializer.Serialize(questions["team"], TypeSafeJsonContext.Default.TypeSafeQuestion);
        var scoreWire = JsonSerializer.Serialize(questions["risk"], TypeSafeJsonContext.Default.TypeSafeQuestion);

        Assert.Equal("""{"type":"noul"}""", noulWire);
        Assert.Equal("""{"type":"choice","criteria":{"support":null,"engineering":null}}""", choiceWire);
        Assert.Equal("""{"type":"score","criteria":["low","high"]}""", scoreWire);
    }

    [Fact]
    public void DuplicateIds_ThrowNamingTheId()
    {
        var exception = Assert.Throws<ArgumentException>(() => Questions.Build(q => q
            .Noul("billing", "First")
            .Noul("billing", "Second")));

        Assert.Contains("billing", exception.Message);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void BlankIds_Throw(string id) =>
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q.Noul(id)));

    [Fact]
    public void DuplicateAndBlankOptionLabels_Throw()
    {
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q
            .Choice("tone", "Tone?", o => o.Option("calm").Option("calm"))));
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q
            .Choice("tone", "Tone?", o => o.Option(" "))));
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q
            .Choice("tone", "Tone?", "calm", "calm")));
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q
            .Choice("tone", "Tone?", "calm", " ")));
    }

    [Fact]
    public void NullReturningLambdasAndEmptyBuilds_Throw()
    {
        Assert.Throws<ArgumentException>(() => Questions.Build(q => null!));
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q.Choice("tone", "Tone?", o => null!)));
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q.Score("risk", "Risk?", s => null!)));
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q));
    }

    [Fact]
    public void ShortRubrics_AreStillCaughtByRequestCreation()
    {
        var questions = Questions.Build(q => q.Score("urgency", "How urgent?", "only"));

        Assert.Throws<ArgumentException>(() => SystemOneRequest.Create("state", questions, "jev-latest"));
    }

    [Fact]
    public void BuiltQuestions_FlowIntoRequestWithoutConversion()
    {
        var request = SystemOneRequest.Create("state",
            Questions.Build(q => q.Noul("billing", "Is this about billing?")), "jev-latest");

        Assert.IsType<Noul>(request.Questions["billing"]);
    }
}

internal record TicketState(string Document, int Attempts);

[JsonSerializable(typeof(TicketState))]
[JsonSerializable(typeof(int))]
internal partial class TestJsonContext : JsonSerializerContext;

public class TypeSafeContentFromObjectTests
{
    [Fact]
    public void FromObject_SerializesThroughTheSuppliedSourceGeneratedMetadata()
    {
        var state = new TicketState("I was charged twice.", 2);

        var content = TypeSafeContent.FromObject(state, TestJsonContext.Default.TicketState);

        Assert.True(JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(state, TestJsonContext.Default.TicketState),
            content.ToJsonNode()));
        Assert.Equal("""{"Document":"I was charged twice.","Attempts":2}""",
            JsonSerializer.Serialize(content, TypeSafeJsonContext.Default.TypeSafeContent));
    }

    [Fact]
    public void FromObject_RejectsNullMetadata()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TypeSafeContent.FromObject(new TicketState("d", 1), null!));
    }

    [Fact]
    public void FromObject_RejectsValuesThatSerializeToScalarRoots()
    {
        // int serializes to a JSON number, which is not a valid content root.
        Assert.Throws<ArgumentException>(() =>
            TypeSafeContent.FromObject(42, TestJsonContext.Default.Int32));
    }
}
