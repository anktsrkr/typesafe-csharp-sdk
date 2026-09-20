using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class TypeSafeClientTests
{
    internal const string SuccessBody = """
        {"model":"jev-latest","answers":{"is_urgent":{"type":"noul","noul":0.92}},"usage":{"input_tokens":10,"output_tokens":2}}
        """;

    internal static TypeSafeClient CreateClient(StubHttpHandler handler, Action<TypeSafeClientOptions>? configure = null)
    {
        var options = new TypeSafeClientOptions { ApiKey = "sk-test" };
        configure?.Invoke(options);
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "sk-test");
        return new TypeSafeClient(client, options);
    }

    internal static IReadOnlyDictionary<string, TypeSafeQuestion> DefaultQuestions() => Questions.Build(q => q
        .Noul("billing", "Is this about billing?")
        .Choice("tone", "What is the tone?", "calm", "angry")
        .Score("urgency", "How urgent is this?", "low", "medium", "high"));

    [Fact]
    public async Task SendsToRelativePathPreservingGatewayPrefix_WithAuthAndJsonBody()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        using var client = CreateClient(handler, o => o.BaseUrl = "https://gw.example.com/typesafe/");

        await client.SystemOneAsync("state", DefaultQuestions());

        var (uri, body, authorization, contentType) = handler.Requests.Single();
        Assert.Equal("https://gw.example.com/typesafe/v1/systemone", uri!.ToString());
        Assert.Equal("Bearer sk-test", authorization);
        Assert.Equal("application/json; charset=utf-8", contentType);

        var expected = JsonSerializer.Serialize(
            SystemOneRequest.Create("state", DefaultQuestions(), "jev-latest"),
            TypeSafeJsonContext.Default.SystemOneRequest);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(Encoding.UTF8.GetString(body))),
            $"Actual body: {Encoding.UTF8.GetString(body)}");
    }

    [Theory]
    [InlineData(null, "jev-latest")]
    [InlineData("jev-mini", "jev-mini")]
    public async Task PerCallModel_OverridesConfiguredDefault(string? perCall, string expectedModel)
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        using var client = CreateClient(handler);

        await client.SystemOneAsync("state", DefaultQuestions(), perCall);

        var sent = JsonNode.Parse(Encoding.UTF8.GetString(handler.Requests.Single().Body))!;
        Assert.Equal(expectedModel, sent["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task CapturesRequestIdHeader()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody, r => r.Headers.Add("x-typesafe-request-id", "req-42"));
        using var client = CreateClient(handler);

        var response = await client.SystemOneAsync("state", DefaultQuestions());

        Assert.Equal("req-42", response.RequestId);
        Assert.Equal(0.92, response.Nouls["is_urgent"].Noul);
    }

    [Theory]
    [InlineData(401, typeof(TypeSafeAuthenticationException))]
    [InlineData(422, typeof(TypeSafeValidationException))]
    [InlineData(429, typeof(TypeSafeRateLimitException))]
    [InlineData(529, typeof(TypeSafeOverloadedException))]
    public async Task DocumentedFailureStatuses_MapToTheirExceptions(int status, Type expected)
    {
        var handler = new StubHttpHandler();
        handler.Enqueue((HttpStatusCode)status);
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAnyAsync<TypeSafeException>(
            () => client.SystemOneAsync("state", DefaultQuestions()));

        Assert.IsType(expected, exception);
        Assert.Equal(status, (int)exception.StatusCode!.Value);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(302)]
    public async Task OtherNonSuccessStatuses_RaiseTheBaseExceptionWithoutARetry(int status)
    {
        var handler = new StubHttpHandler();
        handler.Enqueue((HttpStatusCode)status);
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAnyAsync<TypeSafeException>(
            () => client.SystemOneAsync("state", DefaultQuestions()));

        Assert.Equal(typeof(TypeSafeException), exception.GetType());
        Assert.Equal(status, (int)exception.StatusCode!.Value);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EmptyOrMalformedSuccess_RaisesProtocolException()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "not json");
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<TypeSafeProtocolException>(() => client.SystemOneAsync("state", DefaultQuestions()));
    }

    [Fact]
    public async Task OversizedResponse_RaisesProtocolException()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        using var client = CreateClient(handler, o => o.MaxResponseBytes = 64);

        await Assert.ThrowsAsync<TypeSafeProtocolException>(() => client.SystemOneAsync("state", DefaultQuestions()));
    }

    [Fact]
    public async Task StalledBody_ExpiresWithinTheOperationBudget()
    {
        var handler = new StubHttpHandler();
        handler.EnqueueStalledBody();
        using var client = CreateClient(handler, o => o.Retry.TotalTimeoutBudget = TimeSpan.FromMilliseconds(400));

        var watch = DateTimeOffset.UtcNow;
        var exception = await Assert.ThrowsAsync<TypeSafeTimeoutException>(
            () => client.SystemOneAsync("state", DefaultQuestions()));

        Assert.True(DateTimeOffset.UtcNow - watch < TimeSpan.FromSeconds(5),
            $"The budget abort took {(DateTimeOffset.UtcNow - watch).TotalSeconds:0.#} seconds.");
    }

    [Fact]
    public async Task CallerCancellation_TakesPrecedenceOverTheBudget()
    {
        var handler = new StubHttpHandler();
        handler.EnqueueStalledBody();
        using var client = CreateClient(handler, o => o.Retry.TotalTimeoutBudget = null);
        using var canceller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SystemOneAsync("state", DefaultQuestions(), cancellationToken: canceller.Token));
    }

    [Fact]
    public async Task PreCanceledToken_MakesNoNetworkCall()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        using var client = CreateClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SystemOneAsync("state", DefaultQuestions(), cancellationToken: new CancellationToken(true)));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Redirects_AreNotFollowed()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.Found, configure: r => r.Headers.Location = new Uri("https://api.typesafe.ai/v1/systemone"));
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAnyAsync<TypeSafeException>(
            () => client.SystemOneAsync("state", DefaultQuestions()));

        Assert.Equal(302, (int)exception.StatusCode!.Value);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void StandaloneConstructor_ValidatesOptions()
    {
        var options = new TypeSafeClientOptions { ApiKey = " " };

        Assert.Throws<ArgumentException>(() => new TypeSafeClient(options));
    }

    [Fact]
    public void InvalidOptions_FailAtDiResolution()
    {
        var services = new ServiceCollection();
        services.AddTypeSafeClient(o => o.ApiKey = "");

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ITypeSafeClient>());
    }
}
