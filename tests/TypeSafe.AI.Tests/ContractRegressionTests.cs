using System.Text.Json;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class ContractRegressionTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("{\"model\":null,\"answers\":{},\"usage\":{}}")]
    [InlineData("{\"model\":\"m\",\"answers\":null,\"usage\":{}}")]
    [InlineData("{\"model\":\"m\",\"answers\":{},\"usage\":null}")]
    [InlineData("{\"model\":\"m\",\"answers\":{\"x\":null},\"usage\":{}}")]
    public async Task NullEnvelopeValues_AreProtocolErrors(string json) =>
        await Assert.ThrowsAsync<TypeSafeProtocolException>(() => ResponseDecodeTests.DecodeAsync(json));

    [Theory]
    [InlineData("{\"type\":null}")]
    [InlineData("{\"type\":\"noul\",\"noul\":null}")]
    [InlineData("{\"type\":\"noul\",\"noul\":\"0.5\"}")]
    [InlineData("{\"type\":\"choice\",\"choice\":null,\"probabilities\":{},\"confidence\":1}")]
    [InlineData("{\"type\":\"choice\",\"choice\":\"a\",\"probabilities\":null,\"confidence\":1}")]
    [InlineData("{\"type\":\"choice\",\"choice\":\"a\",\"probabilities\":{\"a\":null},\"confidence\":1}")]
    [InlineData("{\"type\":\"score\",\"score\":1,\"legend\":null,\"probabilities\":{},\"confidence\":1}")]
    [InlineData("{\"type\":\"score\",\"score\":1,\"legend\":{\"0\":null},\"probabilities\":{},\"confidence\":1}")]
    [InlineData("{\"type\":\"score\",\"score\":1,\"legend\":{},\"probabilities\":null,\"confidence\":1}")]
    public async Task InvalidNestedValues_AreProtocolErrors(string answer) =>
        await Assert.ThrowsAsync<TypeSafeProtocolException>(() => ResponseDecodeTests.DecodeAsync(
            "{\"model\":\"m\",\"answers\":{\"x\":" + answer + "},\"usage\":{}}"));

    [Fact]
    public async Task SemanticValues_AreReturnedWithoutRecalculation()
    {
        var response = await ResponseDecodeTests.DecodeAsync("""
            {"model":"m","answers":{
              "n":{"type":"noul","noul":1.5},
              "c":{"type":"choice","choice":"absent","probabilities":{"a":0.1},"confidence":-1},
              "s":{"type":"score","score":100,"legend":{"other":"level"},"probabilities":{"0":0.2},"confidence":2}
            },"usage":{"input_tokens":-1}}
            """);
        Assert.Equal(1.5, response.Nouls["n"].Noul);
        Assert.Equal("absent", response.Choices["c"].Choice);
        Assert.Equal(100, response.Scores["s"].Score);
        Assert.Equal(-1, response.Usage.InputTokens);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public void Choice_ValidBoundaries(int count)
    {
        var labels = Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();
        var built = Questions.Build(q => q.Choice("x", "Choose", labels));
        Assert.Equal(count, Assert.IsType<Choice>(built["x"]).Criteria.Count);
        Assert.Single(SystemOneRequest.Create("state", built, "m").Questions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Choice_InvalidBoundaries_ForBuilderAndRecords(int count)
    {
        var labels = Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q.Choice("x", "Choose", labels)));
        var criteria = labels.ToDictionary(label => label, _ => (TypeSafeContent?)null);
        Assert.Throws<ArgumentException>(() => SystemOneRequest.Create("state",
            new Dictionary<string, TypeSafeQuestion> { ["x"] = new Choice { Instructions = "Choose", Criteria = criteria } }, "m"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public void Score_ValidBoundaries(int count)
    {
        var levels = Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();
        var built = Questions.Build(q => q.Score("x", "Rate", levels));
        Assert.Equal(count, Assert.IsType<Score>(built["x"]).Criteria.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public void Score_InvalidBoundaries_ForBuilderAndRecords(int count)
    {
        var levels = Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q.Score("x", "Rate", levels)));
        Assert.Throws<ArgumentException>(() => SystemOneRequest.Create("state",
            new Dictionary<string, TypeSafeQuestion> { ["x"] = new Score { Instructions = "Rate", Criteria = levels.Select(TypeSafeContent.FromString).ToArray() } }, "m"));
    }

    [Fact]
    public void NullInstructions_AreRejectedForAllQuestionKinds()
    {
        TypeSafeQuestion[] invalid = [
            new Noul { Instructions = null! },
            new Choice { Instructions = null!, Criteria = new Dictionary<string, TypeSafeContent?> { ["a"] = null } },
            new Score { Instructions = null!, Criteria = ["low", "high"] }
        ];
        foreach (var question in invalid)
            Assert.Throws<ArgumentException>(() => SystemOneRequest.Create("state",
                new Dictionary<string, TypeSafeQuestion> { ["x"] = question }, "m"));
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q.Noul("x", (TypeSafeContent)null!)));
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q.Choice("x", (TypeSafeContent)null!, "a")));
        Assert.Throws<ArgumentException>(() => Questions.Build(q => q.Score("x", (TypeSafeContent)null!, "low", "high")));
    }

    [Fact]
    public void RetainedBuilders_CannotMutateCompletedQuestions()
    {
        QuestionBuilder? main = null;
        ChoiceOptions? choice = null;
        ScoreLevels? score = null;
        var built = Questions.Build(q =>
        {
            main = q;
            return q.Choice("c", "Choose", c => { choice = c; return c.Option("a"); })
                .Score("s", "Rate", s => { score = s; return s.Level("low").Level("high"); });
        });
        main!.Noul("later", "Later?");
        choice!.Option("b");
        score!.Level("later");
        Assert.Equal(2, built.Count);
        Assert.Single(Assert.IsType<Choice>(built["c"]).Criteria);
        Assert.Equal(2, Assert.IsType<Score>(built["s"]).Criteria.Count);
    }

    [Fact]
    public void ResponseSnapshotsNestedMaps_AndCachesGroups()
    {
        var probabilities = new Dictionary<string, double> { ["a"] = 1 };
        var legend = new Dictionary<string, string> { ["0"] = "low" };
        var answers = new Dictionary<string, TypeSafeAnswer>
        {
            ["c"] = new ChoiceAnswer { Choice = "a", Probabilities = probabilities, Confidence = 1 },
            ["s"] = new ScoreAnswer { Score = 0, Probabilities = probabilities, Legend = legend, Confidence = 1 }
        };
        var response = new SystemOneResponse("m", answers, new TypeSafeUsage());
        answers.Clear(); probabilities.Clear(); legend.Clear();
        Assert.Equal(2, response.Answers.Count);
        Assert.Single(response.Choices["c"].Probabilities);
        Assert.Single(response.Scores["s"].Legend);
        Assert.Same(response.Nouls, response.Nouls);
        Assert.Same(response.Choices, response.Choices);
        Assert.Same(response.Scores, response.Scores);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, double>)response.Choices["c"].Probabilities).Clear());
    }
}
