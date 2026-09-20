using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class TypeSafeResilienceTests
{
    internal static (ITypeSafeClient Client, StubHttpHandler Handler) CreateClient(Action<TypeSafeClientOptions> configure)
    {
        var handler = new StubHttpHandler();
        var services = new ServiceCollection();
        services.AddTypeSafeClient(configure).ConfigurePrimaryHttpMessageHandler(() => handler);
        var client = services.BuildServiceProvider().GetRequiredService<ITypeSafeClient>();
        return (client, handler);
    }

    internal static Action<TypeSafeClientOptions> FastRetry(int maxRetries = 2) => options =>
    {
        options.ApiKey = "sk-di";
        options.Retry.MaxRetries = maxRetries;
        options.Retry.BackoffInitial = TimeSpan.FromMilliseconds(1);
        options.Retry.UseJitter = false;
    };

    [Fact]
    public async Task Transient429s_AreRetried_AndReplayIdenticalBytes()
    {
        var (client, handler) = CreateClient(FastRetry());
        handler.Enqueue(HttpStatusCode.TooManyRequests);
        handler.Enqueue(HttpStatusCode.TooManyRequests);
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);

        var response = await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());

        Assert.Equal(0.92, response.Nouls["is_urgent"].Noul);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.True(
            request.Body.AsSpan().SequenceEqual(handler.Requests[0].Body),
            "Retried requests must carry identical bytes."));
    }

    [Theory]
    [InlineData(429, typeof(TypeSafeRateLimitException))]
    [InlineData(529, typeof(TypeSafeOverloadedException))]
    [InlineData(500, typeof(TypeSafeException))]
    public async Task ExhaustedRetries_MapToDocumentedExceptions(int status, Type expected)
    {
        var (client, handler) = CreateClient(FastRetry());
        handler.Enqueue((HttpStatusCode)status);
        handler.Enqueue((HttpStatusCode)status);
        handler.Enqueue((HttpStatusCode)status);

        var exception = await Assert.ThrowsAnyAsync<TypeSafeException>(
            () => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions()));

        Assert.IsType(expected, exception);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(401, typeof(TypeSafeAuthenticationException))]
    [InlineData(422, typeof(TypeSafeValidationException))]
    public async Task AuthenticationAndValidationFailures_AreNotRetried(int status, Type expected)
    {
        var (client, handler) = CreateClient(FastRetry());
        handler.Enqueue((HttpStatusCode)status);

        var exception = await Assert.ThrowsAnyAsync<TypeSafeException>(
            () => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions()));

        Assert.IsType(expected, exception);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TransportFailures_AreRetried()
    {
        var (client, handler) = CreateClient(FastRetry());
        handler.EnqueueThrow(new HttpRequestException("connection reset"));
        handler.EnqueueThrow(new HttpRequestException("connection reset"));
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);

        var response = await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());

        Assert.NotNull(response);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task TransportFailures_ThatPersist_RaiseConnectionException()
    {
        var (client, handler) = CreateClient(FastRetry(maxRetries: 1));
        handler.EnqueueThrow(new HttpRequestException("connection reset"));
        handler.EnqueueThrow(new HttpRequestException("connection reset"));

        var exception = await Assert.ThrowsAsync<TypeSafeConnectionException>(
            () => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions()));

        Assert.NotNull(exception.InnerException);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RetryAfterSeconds_IsHonored()
    {
        var (client, handler) = CreateClient(FastRetry());
        handler.Enqueue(HttpStatusCode.TooManyRequests, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1)));
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);

        await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());

        var gap = handler.AttemptTimes[1] - handler.AttemptTimes[0];
        Assert.True(gap >= TimeSpan.FromSeconds(0.7), $"Second attempt started after {gap.TotalMilliseconds} ms.");
    }

    [Fact]
    public async Task RetryAfterHttpDate_IsHonored()
    {
        var (client, handler) = CreateClient(FastRetry());
        handler.Enqueue(HttpStatusCode.TooManyRequests,
            configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(1)));
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);

        await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());

        var gap = handler.AttemptTimes[1] - handler.AttemptTimes[0];
        Assert.True(gap >= TimeSpan.FromSeconds(0.7), $"Second attempt started after {gap.TotalMilliseconds} ms.");
    }

    [Fact]
    public async Task TotalBudget_AbortsALongRetryAfter_WithoutRetryingEarlier()
    {
        var (client, handler) = CreateClient(o =>
        {
            FastRetry()(o);
            o.Retry.TotalTimeoutBudget = TimeSpan.FromMilliseconds(300);
        });
        handler.Enqueue(HttpStatusCode.TooManyRequests, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);

        var watch = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<TypeSafeTimeoutException>(
            () => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions()));

        Assert.True(DateTimeOffset.UtcNow - watch < TimeSpan.FromSeconds(5),
            $"The budget abort took {(DateTimeOffset.UtcNow - watch).TotalSeconds:0.#} seconds.");
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellation_DuringBackoff_SurfacesAsOperationCanceled()
    {
        var (client, handler) = CreateClient(o =>
        {
            FastRetry()(o);
            o.Retry.MaxRetries = 3;
            o.Retry.BackoffInitial = TimeSpan.FromSeconds(5);
        });
        handler.Enqueue(HttpStatusCode.TooManyRequests);
        using var canceller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions(), cancellationToken: canceller.Token));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PerAttemptTimeout_Retries_ThenRaisesTimeout()
    {
        var (client, handler) = CreateClient(o =>
        {
            FastRetry(maxRetries: 1)(o);
            o.Retry.PerAttemptTimeout = TimeSpan.FromMilliseconds(200);
        });
        handler.EnqueueStalledHeaders(5_000);
        handler.EnqueueStalledHeaders(5_000);

        await Assert.ThrowsAsync<TypeSafeTimeoutException>(
            () => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions()));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ZeroRetries_DisablesTheRetryStrategy()
    {
        var (client, handler) = CreateClient(FastRetry(maxRetries: 0));
        handler.Enqueue(HttpStatusCode.TooManyRequests);

        var exception = await Assert.ThrowsAsync<TypeSafeRateLimitException>(
            () => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions()));

        Assert.NotNull(exception);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DiRegistration_SendsTheConfiguredBearerCredential()
    {
        var (client, handler) = CreateClient(FastRetry());
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);

        await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());

        Assert.Equal("Bearer sk-di", handler.Requests.Single().Authorization);
    }
}
