using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class TransportRegressionTests
{
    [Theory]
    [InlineData(200, false)]
    [InlineData(200, true)]
    [InlineData(422, false)]
    public async Task MessagesAreDisposed_ButTheTransportRemainsCallerOwned(int status, bool malformed)
    {
        var handler = new TrackingHandler((HttpStatusCode)status,
            malformed ? "not JSON" : TypeSafeClientTests.SuccessBody);
        using (var http = new HttpClient(handler))
        {
            var client = new TypeSafeClient(http, new TypeSafeClientOptions { ApiKey = "test" });
            if (status == 200 && !malformed)
                await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());
            else
                await Assert.ThrowsAnyAsync<TypeSafeException>(() => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions()));
            Assert.False(handler.Disposed);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Response.Content.ReadAsStringAsync());
        }
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task SharedInjectedClient_KeepsOwnershipSettingsAndCredentialsIsolated()
    {
        using var handler = new StubHttpHandler();
        for (var i = 0; i < 3; i++) handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://unused.example/"), Timeout = TimeSpan.FromSeconds(50) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "original");
        var first = new TypeSafeClient(http, new TypeSafeClientOptions { ApiKey = "first" });
        var second = new TypeSafeClient(http, new TypeSafeClientOptions { ApiKey = "second" });
        await first.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());
        await second.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());
        await first.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());
        Assert.Equal(new[] { "Bearer first", "Bearer second", "Bearer first" }, handler.Requests.Select(r => r.Authorization));
        Assert.Equal("Bearer original", http.DefaultRequestHeaders.Authorization!.ToString());
        Assert.Equal("https://unused.example/", http.BaseAddress!.ToString());
        Assert.Equal(TimeSpan.FromSeconds(50), http.Timeout);
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(TypeSafeClient)));
    }

    [Fact]
    public async Task DirectClient_SnapshotsOptions()
    {
        using var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);
        using var http = new HttpClient(handler);
        var options = new TypeSafeClientOptions { ApiKey = "before", DefaultModel = "before", BaseUrl = "https://before.example/" };
        var client = new TypeSafeClient(http, options);
        options.ApiKey = "after"; options.DefaultModel = "after"; options.BaseUrl = "https://after.example/";
        options.Retry.TotalTimeoutBudget = TimeSpan.Zero;
        await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("before.example", sent.Uri!.Host);
        Assert.Equal("Bearer before", sent.Authorization);
        using var json = JsonDocument.Parse(sent.Body);
        Assert.Equal("before", json.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task DiClientAndPipeline_UseTheSameSnapshot()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.TooManyRequests);
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);
        var services = new ServiceCollection();
        services.AddTypeSafeClient(o =>
        {
            o.ApiKey = "before"; o.Retry.MaxRetries = 1;
            o.Retry.BackoffInitial = TimeSpan.FromMilliseconds(1); o.Retry.UseJitter = false;
        }).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<TypeSafeClientSettings>();
        var options = provider.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value;
        options.ApiKey = "after"; options.DefaultModel = "after";
        options.Retry.MaxRetries = 0; options.Retry.TotalTimeoutBudget = TimeSpan.Zero;
        var client = provider.GetRequiredService<ITypeSafeClient>();
        await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("Bearer before", r.Authorization));
        Assert.Same(settings, provider.GetRequiredService<TypeSafeClientSettings>());
    }

    [Fact]
    public async Task LargeSuccessfulResponse_HasNoSdkByteCap()
    {
        using var handler = new StubHttpHandler();
        var body = TypeSafeClientTests.SuccessBody.TrimEnd();
        body = body[..^1] + ",\"additional\":\"" + new string('x', 4_194_305) + "\"}";
        handler.Enqueue(HttpStatusCode.OK, body);
        using var http = new HttpClient(handler);
        var client = new TypeSafeClient(http, new TypeSafeClientOptions { ApiKey = "test" });
        var result = await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());
        Assert.Equal(0.92, result.Nouls["is_urgent"].Noul);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"extra\":{\"type\":\"noul\",\"noul\":0.5}}")]
    [InlineData("{\"is_urgent\":{\"type\":\"choice\",\"choice\":\"a\",\"probabilities\":{},\"confidence\":1}}")]
    public async Task ResponseIdsAndKinds_AreReturnedAsReceived(string answers)
    {
        using var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"model\":\"m\",\"answers\":" + answers + ",\"usage\":{}}");
        using var http = new HttpClient(handler);
        var client = new TypeSafeClient(http, new TypeSafeClientOptions { ApiKey = "test" });
        var response = await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());
        using var json = JsonDocument.Parse(answers);
        Assert.Equal(json.RootElement.EnumerateObject().Select(p => p.Name), response.Answers.Keys);
    }

    [Theory]
    [InlineData(401, "plain text")]
    [InlineData(422, "{\"detail\":\"bad field\"}")]
    [InlineData(429, "{ invalid json")]
    [InlineData(529, "")]
    [InlineData(500, "upstream error")]
    public async Task HttpErrors_PreserveMetadataWithoutLeakingBodies(int status, string body)
    {
        using var handler = new StubHttpHandler();
        handler.Enqueue((HttpStatusCode)status, body, response =>
        {
            response.Headers.Add("x-typesafe-request-id", "req-error");
            response.Headers.TryAddWithoutValidation("Retry-After", "7");
        });
        using var http = new HttpClient(handler);
        var client = new TypeSafeClient(http, new TypeSafeClientOptions { ApiKey = "credential-secret" });
        var error = await Assert.ThrowsAnyAsync<TypeSafeException>(() => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions()));
        Assert.Equal(status, (int)error.StatusCode!);
        Assert.Equal("req-error", error.RequestId);
        Assert.Equal(body.Length == 0 ? null : body, error.ResponseBody);
        Assert.Equal("7", error.RetryAfter);
        Assert.DoesNotContain("credential-secret", error.ToString());
        if (body.Length > 0) Assert.DoesNotContain(body, error.ToString());
    }

    [Theory]
    [InlineData("AllowInsecureHttp", "perhaps")]
    [InlineData("Retry:MaxRetries", "invalid")]
    [InlineData("Retry:BackoffInitial", "invalid")]
    [InlineData("Retry:BackoffMax", "invalid")]
    [InlineData("Retry:UseJitter", "invalid")]
    [InlineData("Retry:RespectRetryAfter", "invalid")]
    [InlineData("Retry:PerAttemptTimeout", "invalid")]
    [InlineData("Retry:TotalTimeoutBudget", "invalid")]
    public void MalformedConfiguration_NamesTheFullKey(string key, string value)
    {
        var fullKey = "TypeSafe:" + key;
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [fullKey] = value }).Build();
        services.AddTypeSafeClient(config, o => o.ApiKey = "test");
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ITypeSafeClient>());
        Assert.Contains(fullKey, error.Message);
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("DefaultModel")]
    [InlineData("BaseUrl")]
    public void BlankConfiguredRequiredStrings_FailValidation(string key)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["TypeSafe:ApiKey"] = "test", ["TypeSafe:" + key] = "" }).Build();
        services.AddTypeSafeClient(config);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ITypeSafeClient>());
    }

    [Fact]
    public void EmptyConfiguredTimeouts_DisableBothBudgets()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["TypeSafe:Retry:PerAttemptTimeout"] = "", ["TypeSafe:Retry:TotalTimeoutBudget"] = "" }).Build();
        services.AddTypeSafeClient(config, o => o.ApiKey = "test");
        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<TypeSafeClientSettings>();
        Assert.Null(settings.PerAttemptTimeout);
        Assert.Null(settings.TotalTimeoutBudget);
    }

    [Theory]
    [InlineData("TypeSafe:UnknownKey", "val")]
    [InlineData("TypeSafe:Retry:UnknownRetrySetting", "val")]
    public void UnrecognizedConfigurationKeys_ThrowOptionsValidationException(string key, string value)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { [key] = value, ["TypeSafe:ApiKey"] = "test" }).Build();
        services.AddTypeSafeClient(config);
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ITypeSafeClient>());
        Assert.Contains(key, error.Message);
        Assert.Contains("Unrecognized configuration key", error.Message);
    }

    private sealed class TrackingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public bool Disposed { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public HttpResponseMessage Response { get; } = new(status) { Content = new StringContent(body) };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Request = request;
            return Task.FromResult(Response);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
