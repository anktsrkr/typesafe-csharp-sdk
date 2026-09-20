using System.Text.Json.Nodes;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class RequestValidationTests
{
    [Fact]
    public void Create_RejectsNullInputs()
    {
        var questions = new Dictionary<string, TypeSafeQuestion> { ["billing"] = new Noul() };

        Assert.Throws<ArgumentNullException>(() => SystemOneRequest.Create(null!, questions, "jev-latest"));
        Assert.Throws<ArgumentNullException>(() => SystemOneRequest.Create("state", null!, "jev-latest"));
        Assert.Throws<ArgumentNullException>(() => SystemOneRequest.Create("state", questions, null!));
    }

    [Fact]
    public void Create_RejectsEmptyQuestionsAndBlankIdsAndBlankModel()
    {
        Assert.Throws<ArgumentException>(() =>
            SystemOneRequest.Create("state", new Dictionary<string, TypeSafeQuestion>(), "jev-latest"));
        Assert.Throws<ArgumentException>(() =>
            SystemOneRequest.Create("state", new Dictionary<string, TypeSafeQuestion> { [" "] = new Noul() }, "jev-latest"));
        Assert.Throws<ArgumentException>(() =>
            SystemOneRequest.Create("state", new Dictionary<string, TypeSafeQuestion> { ["billing"] = new Noul() }, " "));
    }

    [Fact]
    public void Create_RejectsEmptyChoiceCriteria()
    {
        var questions = new Dictionary<string, TypeSafeQuestion>
        {
            ["tone"] = new Choice { Criteria = new Dictionary<string, TypeSafeContent?>() }
        };

        Assert.Throws<ArgumentException>(() => SystemOneRequest.Create("state", questions, "jev-latest"));
    }

    [Fact]
    public void Create_RejectsScoreRubricsWithFewerThanTwoLevels()
    {
        var questions = new Dictionary<string, TypeSafeQuestion>
        {
            ["urgency"] = new Score { Criteria = ["low"] }
        };

        Assert.Throws<ArgumentException>(() => SystemOneRequest.Create("state", questions, "jev-latest"));
    }

    [Fact]
    public void Create_RejectsNullScoreLevelsAndNullQuestions()
    {
        var levels = new List<TypeSafeContent> { null!, "high" };
        Assert.Throws<ArgumentException>(() => SystemOneRequest.Create("state",
            new Dictionary<string, TypeSafeQuestion> { ["urgency"] = new Score { Criteria = levels } }, "jev-latest"));
        Assert.Throws<ArgumentException>(() => SystemOneRequest.Create("state",
            new Dictionary<string, TypeSafeQuestion> { ["billing"] = null! }, "jev-latest"));
    }

    [Fact]
    public void Create_RejectsUnsupportedQuestionTypes()
    {
        Assert.Throws<ArgumentException>(() => SystemOneRequest.Create("state",
            new Dictionary<string, TypeSafeQuestion> { ["odd"] = new BogusQuestion() }, "jev-latest"));
    }

    [Fact]
    public void Create_AcceptsATwoLevelRubric()
    {
        var request = SystemOneRequest.Create("state",
            new Dictionary<string, TypeSafeQuestion> { ["urgency"] = new Score { Criteria = ["low", "high"] } }, "jev-latest");

        Assert.Single(request.Questions);
    }

    [Fact]
    public void Create_SnapshotsMutableInputs()
    {
        var state = new JsonObject { ["ticket"] = "t-1" };
        var criteria = new Dictionary<string, TypeSafeContent?> { ["calm"] = null };
        var levels = new List<TypeSafeContent> { "low", "high" };
        var questions = new Dictionary<string, TypeSafeQuestion>
        {
            ["tone"] = new Choice { Criteria = criteria },
            ["urgency"] = new Score { Criteria = levels }
        };

        var request = SystemOneRequest.Create(state, questions, "jev-latest");

        questions["sneaky"] = new Noul();
        criteria["angry"] = null;
        state["ticket"] = "t-2";
        levels.Add("medium");

        var wire = Wire(request);
        Assert.DoesNotContain("sneaky", wire);
        Assert.DoesNotContain("angry", wire);
        Assert.DoesNotContain("t-2", wire);
        Assert.DoesNotContain("medium", wire);
    }

    private static string Wire(SystemOneRequest request) =>
        System.Text.Json.JsonSerializer.Serialize(request, TypeSafeJsonContext.Default.SystemOneRequest);

    private sealed record BogusQuestion : TypeSafeQuestion;
}
