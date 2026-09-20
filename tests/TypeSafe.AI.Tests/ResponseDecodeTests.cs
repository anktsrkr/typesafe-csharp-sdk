using System.Text.Json;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class ResponseDecodeTests
{
    [Fact]
    public async Task DocumentedNoulResponse_DecodesWithGroupedMapsAndRequestId()
    {
        var response = await DecodeAsync("""
            {"model":"jev-latest","answers":{"is_urgent":{"type":"noul","noul":0.92}},"usage":{"input_tokens":312,"output_tokens":48}}
            """, requestId: "req-1");

        Assert.Equal("jev-latest", response.Model);
        Assert.Equal("req-1", response.RequestId);
        Assert.Equal(0.92, response.Nouls["is_urgent"].Noul);
        Assert.Equal(312, response.Usage.InputTokens);
        Assert.Equal(48, response.Usage.OutputTokens);
        Assert.Empty(response.Choices);
        Assert.Empty(response.Scores);
    }

    [Fact]
    public async Task DocumentedChoiceResponse_DecodesTopOptionDistributionAndConfidence()
    {
        var response = await DecodeAsync("""
            {"model":"jev-latest","answers":{"department":{"type":"choice","choice":"technical","probabilities":{"billing":0.08,"technical":0.85,"sales":0.07},"confidence":0.82}},"usage":{"input_tokens":300,"output_tokens":40}}
            """);

        var department = Assert.IsType<ChoiceAnswer>(response.Answers["department"]);
        Assert.Equal("technical", response.Choices["department"].Choice);
        Assert.Equal(0.85, department.Probabilities["technical"]);
        Assert.Equal(0.82, department.Confidence);
        Assert.Empty(response.Nouls);
        Assert.Empty(response.Scores);
    }

    [Fact]
    public async Task DocumentedScoreResponse_DecodesScoreLegendAndDistribution()
    {
        var response = await DecodeAsync("""
            {"model":"jev-latest","answers":{"tone":{"type":"score","score":1.6,"legend":{"0":"Calm","1":"Frustrated","2":"Very angry"},"probabilities":{"0":0.05,"1":0.3,"2":0.65},"confidence":0.78}},"usage":{"input_tokens":250,"output_tokens":35}}
            """);

        var tone = Assert.IsType<ScoreAnswer>(response.Answers["tone"]);
        Assert.Equal(1.6, response.Scores["tone"].Score);
        Assert.Equal("Very angry", tone.Legend["2"]);
        Assert.Equal(0.05, tone.Probabilities["0"]);
        Assert.Equal(0.78, tone.Confidence);
    }

    [Fact]
    public async Task MixedResponses_GroupByTypeAndKeepUnknownKinds()
    {
        var response = await DecodeAsync("""
            {
              "model": "jev-latest",
              "answers": {
                "billing": {"type": "noul", "noul": 0.9},
                "tone": {"type": "choice", "choice": "angry", "probabilities": {"calm": 0.2, "angry": 0.8}, "confidence": 0.7},
                "urgency": {"type": "score", "score": 1.0, "legend": {"0": "low", "1": "high"}, "probabilities": {"0": 0.4, "1": 0.6}, "confidence": 0.6},
                "screenshot": {"type": "vision", "url": "https://example.com/attachment.png"}
              },
              "usage": {"input_tokens": 100, "output_tokens": 10}
            }
            """);

        Assert.Equal(4, response.Answers.Count);
        Assert.Single(response.Nouls);
        Assert.Single(response.Choices);
        Assert.Single(response.Scores);

        var unknown = Assert.IsType<UnknownAnswer>(response.Answers["screenshot"]);
        Assert.Equal("vision", unknown.Type);
        Assert.Equal("https://example.com/attachment.png", unknown.Raw.GetProperty("url").GetString());
        Assert.False(response.Nouls.ContainsKey("screenshot"));
    }

    [Fact]
    public async Task MissingIds_AreAbsentKeysWithoutErrors()
    {
        var response = await DecodeAsync("""
            {"model":"jev-latest","answers":{"is_urgent":{"type":"noul","noul":0.92}},"usage":{}}
            """);

        Assert.False(response.Nouls.TryGetValue("missing", out _));
        Assert.False(response.Choices.TryGetValue("is_urgent", out _));
    }

    [Fact]
    public async Task NullableUsageTokens_DecodeAsNull()
    {
        var response = await DecodeAsync("""
            {"model":"jev-latest","answers":{"is_urgent":{"type":"noul","noul":0.92}},"usage":{"input_tokens":null,"output_tokens":null}}
            """);

        Assert.Null(response.Usage.InputTokens);
        Assert.Null(response.Usage.OutputTokens);
    }

    [Fact]
    public async Task AdditiveFields_ArePermitted()
    {
        var response = await DecodeAsync("""
            {"model":"jev-latest","answers":{"is_urgent":{"type":"noul","noul":0.92,"note":"extra"}},"usage":{},"note":"additive"}
            """);

        Assert.Equal(0.92, response.Nouls["is_urgent"].Noul);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{"answers":{},"usage":{}}""")]
    [InlineData("""{"model":"jev-latest","usage":{}}""")]
    [InlineData("""{"model":"jev-latest","answers":{}}""")]
    [InlineData("""{"model":"jev-latest","answers":{"x":5},"usage":{}}""")]
    [InlineData("""{"model":"jev-latest","answers":{"x":{"noul":0.5}},"usage":{}}""")]
    [InlineData("""{"model":"jev-latest","answers":{"x":{"type":"noul"}},"usage":{}}""")]
    [InlineData("""{"model":"jev-latest","answers":{"x":{"type":"noul","noul":0.5}},"usage":{"input_tokens":312.5}}""")]
    [InlineData("""{"model":"jev-latest","answers":{"x":{"type":"noul","noul":0.5}},"usage":{"input_tokens":"312"}}""")]
    public async Task MalformedResponses_RaiseProtocolExceptions(string payload) =>
        await Assert.ThrowsAsync<TypeSafeProtocolException>(() => DecodeAsync(payload));
    internal static async Task<SystemOneResponse> DecodeAsync(string json, string? requestId = null)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return await TypeSafeResponseReader.ReadAsync(stream, requestId, default);
    }}
