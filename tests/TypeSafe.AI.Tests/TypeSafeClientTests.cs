using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
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
        {"model":"jev-latest","answers":{"is_urgent":{"type":"noul","noul":0.92},"tone":{"type":"choice","choice":"calm","probabilities":{"calm":0.8,"angry":0.2},"confidence":0.7},"urgency":{"type":"score","score":1.0,"legend":{"0":"low","1":"medium","2":"high"},"probabilities":{"0":0.1,"1":0.8,"2":0.1},"confidence":0.7}},"usage":{"input_tokens":10,"output_tokens":2}}
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
        .Noul("is_urgent", "Is this urgent?")
        .Choice("tone", "What is the tone?", "calm", "angry")
        .Score("urgency", "How urgent is this?", "low", "medium", "high"));

    [Fact]
    public async Task SendsToRelativePathPreservingGatewayPrefix_WithAuthAndJsonBody()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        var client = CreateClient(handler, o => o.BaseUrl = "https://gw.example.com/typesafe/");

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
        var client = CreateClient(handler);

        await client.SystemOneAsync("state", DefaultQuestions(), perCall);

        var sent = JsonNode.Parse(Encoding.UTF8.GetString(handler.Requests.Single().Body))!;
        Assert.Equal(expectedModel, sent["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task CapturesRequestIdHeader()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody, r => r.Headers.Add("x-typesafe-request-id", "req-42"));
        var client = CreateClient(handler);

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
        var client = CreateClient(handler);

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
        var client = CreateClient(handler);

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
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<TypeSafeProtocolException>(() => client.SystemOneAsync("state", DefaultQuestions()));
    }

    [Fact]
    public async Task StalledBody_ExpiresWithinTheOperationBudget()
    {
        var handler = new StubHttpHandler();
        handler.EnqueueStalledBody();
        var client = CreateClient(handler, o => o.Retry.TotalTimeoutBudget = TimeSpan.FromMilliseconds(400));

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
        var client = CreateClient(handler, o => o.Retry.TotalTimeoutBudget = null);
        using var canceller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SystemOneAsync("state", DefaultQuestions(), cancellationToken: canceller.Token));
    }

    [Fact]
    public async Task PreCanceledToken_MakesNoNetworkCall()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        var client = CreateClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SystemOneAsync("state", DefaultQuestions(), cancellationToken: new CancellationToken(true)));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Redirects_AreNotFollowed()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.Found, configure: r => r.Headers.Location = new Uri("https://api.typesafe.ai/v1/systemone"));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAnyAsync<TypeSafeException>(
            () => client.SystemOneAsync("state", DefaultQuestions()));

        Assert.Equal(302, (int)exception.StatusCode!.Value);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void InjectedConstructor_ValidatesOptions()
    {
        var options = new TypeSafeClientOptions { ApiKey = " " };

        using var http = new HttpClient(new StubHttpHandler());
        Assert.Throws<ArgumentException>(() => new TypeSafeClient(http, options));
    }

    [Fact]
    public void InvalidOptions_FailAtDiResolution()
    {
        var services = new ServiceCollection();
        services.AddTypeSafeClient(o => o.ApiKey = "");

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ITypeSafeClient>());
    }

    [Fact]
    public void FactoryCreatedClient_DisposesOwnedHttpClient()
    {
        var options = new TypeSafeClientOptions { ApiKey = "sk-test" };
        var client = TypeSafeClient.Create(options);

        client.Dispose();

        // After disposal, further calls must throw ObjectDisposedException.
        Assert.Throws<ObjectDisposedException>(() =>
            client.SystemOneAsync("state", DefaultQuestions()).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task CallerOwnedClient_DoesNotDisposeTheSuppliedHttpClient()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new TypeSafeClient(http, new TypeSafeClientOptions { ApiKey = "sk-test" });

        client.Dispose();

        // The HttpClient must still be usable because the caller owns it.
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        var response = await http.GetAsync("https://example.com");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DisposedClient_ThrowsObjectDisposedException()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        var client = CreateClient(handler);

        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.SystemOneAsync("state", DefaultQuestions()));
    }

    [Fact]
    public void ActivitySourceVersion_MatchesProjectVersion()
    {
        Assert.False(string.IsNullOrWhiteSpace(TypeSafeDiagnostics.SourceVersion));
        Assert.Matches(@"^\d+\.\d+\.\d+", TypeSafeDiagnostics.SourceVersion);
    }

    [Fact]
    public async Task SendsUserAgentHeader_WithVersionAndProduct()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        var client = CreateClient(handler);

        await client.SystemOneAsync("state", DefaultQuestions());

        var request = handler.SentMessages.Single();
        var userAgent = request.Headers.UserAgent.ToString();
        Assert.Equal($"typesafe-dotnet/{TypeSafeDiagnostics.SourceVersion}", userAgent);
    }

    [Fact]
    public async Task SystemOneAsync_AcceptsPreBuiltSystemOneRequest()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, SuccessBody);
        var client = CreateClient(handler);

        var preBuilt = SystemOneRequest.Create("pre-built-state", DefaultQuestions(), "custom-model");
        var response = await client.SystemOneAsync(preBuilt);

        Assert.Equal(0.92, response.Nouls["is_urgent"].Noul);
        var sent = JsonNode.Parse(Encoding.UTF8.GetString(handler.Requests.Single().Body))!;
        Assert.Equal("custom-model", sent["model"]!.GetValue<string>());
        Assert.Equal("pre-built-state", sent["state"]!.GetValue<string>());
    }
}
