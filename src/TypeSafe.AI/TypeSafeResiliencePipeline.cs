using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace TypeSafe.AI;

/// <summary>Retry wrapping header-phase attempt timeouts. The client owns the total deadline.</summary>
internal static class TypeSafeResiliencePipeline
{
    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder, TypeSafeClientSettings settings)
    {
        if (settings.MaxRetries > 0)
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                Name = "typesafe-retry",
                MaxRetryAttempts = settings.MaxRetries,
                Delay = settings.BackoffInitial,
                MaxDelay = settings.BackoffMax,
                UseJitter = settings.UseJitter,
                ShouldRetryAfterHeader = settings.RespectRetryAfter
            });
        if (settings.PerAttemptTimeout is { } attempt)
            builder.AddTimeout(new HttpTimeoutStrategyOptions { Name = "typesafe-attempt", Timeout = attempt });
    }
}
