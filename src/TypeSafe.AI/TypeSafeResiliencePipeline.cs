using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace TypeSafe.AI;

/// <summary>Configures the single Polly resilience pipeline used by the TypeSafe HTTP client:
/// total timeout wrapping retry wrapping per-attempt timeout. Disabled strategies are omitted,
/// never configured with infinite durations.</summary>
/// <remarks>Public so consumers with custom transports can mirror the client's handler chain.</remarks>
public static class TypeSafeResiliencePipeline
{
    /// <summary>Adds the TypeSafe strategies to a response pipeline builder in outermost-first order.</summary>
    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder, TypeSafeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        var retry = options.Retry;

        if (retry.TotalTimeoutBudget is { } budget)
            builder.AddTimeout(new HttpTimeoutStrategyOptions { Name = "typesafe-total", Timeout = budget });

        if (retry.MaxRetries > 0)
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                Name = "typesafe-retry",
                MaxRetryAttempts = retry.MaxRetries,
                Delay = retry.BackoffInitial,
                MaxDelay = retry.BackoffMax,
                UseJitter = retry.UseJitter,
                ShouldRetryAfterHeader = retry.RespectRetryAfter
            });

        if (retry.PerAttemptTimeout is { } attempt)
            builder.AddTimeout(new HttpTimeoutStrategyOptions { Name = "typesafe-attempt", Timeout = attempt });
    }
}
