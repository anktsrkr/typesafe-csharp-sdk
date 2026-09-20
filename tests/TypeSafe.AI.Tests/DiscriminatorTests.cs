using System.Text.Json;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class DiscriminatorTests
{
    [Fact]
    public void Discriminator_AfterOtherProperties_StillDeserializes()
    {
        const string json = """{"criteria":["can wait","this week","today"],"instructions":"How urgent is this?","type":"score"}""";

        var question = JsonSerializer.Deserialize(json, TypeSafeJsonContext.Default.TypeSafeQuestion);

        var score = Assert.IsType<Score>(question);
        Assert.Equal("can wait", score.Criteria[0].ToJsonNode().GetValue<string>());
        Assert.Equal("today", score.Criteria[2].ToJsonNode().GetValue<string>());
    }

    [Fact]
    public void QuestionDictionary_RoundTrip_PreservesQuestionTypes()
    {
        var questions = new Dictionary<string, TypeSafeQuestion>
        {
            ["billing"] = new Noul { Instructions = "Is this about billing?" },
            ["tone"] = new Choice
            {
                Instructions = "What is the tone?",
                Criteria = new Dictionary<string, TypeSafeContent?> { ["calm"] = null, ["angry"] = null }
            },
            ["urgency"] = new Score { Instructions = "How urgent is this?", Criteria = ["low", "medium", "high"] }
        };

        var serialized = JsonSerializer.Serialize(questions, TypeSafeJsonContext.Default.IReadOnlyDictionaryStringTypeSafeQuestion);
        var roundTrip = JsonSerializer.Deserialize(serialized, TypeSafeJsonContext.Default.IReadOnlyDictionaryStringTypeSafeQuestion)!;

        Assert.IsType<Noul>(roundTrip["billing"]);
        Assert.IsType<Choice>(roundTrip["tone"]);
        Assert.IsType<Score>(roundTrip["urgency"]);
        Assert.Equal(3, Assert.IsType<Score>(roundTrip["urgency"]).Criteria.Count);
    }
}
