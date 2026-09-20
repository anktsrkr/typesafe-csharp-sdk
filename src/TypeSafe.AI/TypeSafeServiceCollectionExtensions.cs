using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

namespace TypeSafe.AI;

/// <summary>Framework validation adapter over <see cref="TypeSafeClientOptions.Validate"/> so the
/// options pipeline (and <c>ValidateOnStart</c>) enforces the same rules as standalone construction.</summary>
internal sealed class TypeSafeOptionsValidator : IValidateOptions<TypeSafeClientOptions>
{
    public ValidateOptionsResult Validate(string? name, TypeSafeClientOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}

/// <summary>Registers <see cref="ITypeSafeClient"/> through the options pattern on a named
/// HttpClient carrying the TypeSafe Polly resilience pipeline.</summary>
/// <remarks>
/// Precedence: library defaults, environment aliases (TYPESAFE_API_KEY, TYPESAFE_BASE_URL,
/// TYPESAFE_DEFAULT_MODEL), the host's <c>TypeSafe</c> configuration section, then the callback.
/// The typed client is transient; do not capture it in an application singleton.
/// </remarks>
public static class TypeSafeServiceCollectionExtensions
{
    /// <summary>Adds the TypeSafe client and returns the HTTP builder so hosts can customize handlers.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Optional host configuration; its <c>TypeSafe</c> section is bound onto the options.</param>
    /// <param name="configure">Final callback, applied after all other configuration sources.</param>
    public static IHttpClientBuilder AddTypeSafeClient(this IServiceCollection services,
        IConfiguration? configuration = null, Action<TypeSafeClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<TypeSafeClientOptions>()
            .Configure(static options => options.ApplyEnvironmentVariables());
        if (configuration is not null)
        {
            // Explicit, reflection-free section parsing: the configuration binder relies on
            // runtime reflection and this library must stay NativeAOT-clean. Unset keys keep
            // their defaults; an empty string for an optional duration disables it.
            optionsBuilder.Configure(options => ApplyConfigurationSection(options, configuration.GetSection("TypeSafe")));
        }
        if (configure is not null)
            optionsBuilder.Configure(configure);
        optionsBuilder.ValidateOnStart();

        // The options pipeline validates on first resolution and at host startup via IValidateOptions.
        services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IValidateOptions<TypeSafeClientOptions>, TypeSafeOptionsValidator>());

        var httpClientBuilder = services.AddHttpClient(TypeSafeClient.HttpClientName, static (serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                // The Polly strategies own all timing; HttpClient must not cut attempts short.
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            });

        // AddResilienceHandler returns the pipeline builder in this version, so the typed client
        // is registered on the HTTP builder itself.
        httpClientBuilder.AddResilienceHandler("typesafe", static (pipeline, context) =>
            TypeSafeResiliencePipeline.Configure(pipeline,
                context.ServiceProvider.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value));

        return httpClientBuilder.AddTypedClient<ITypeSafeClient>(static (client, serviceProvider) =>
            new TypeSafeClient(client, serviceProvider.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value));
    }

    /// <summary>Configures only through the callback: environment aliases are applied first, then <paramref name="configure"/>.</summary>
    public static IHttpClientBuilder AddTypeSafeClient(this IServiceCollection services,
        Action<TypeSafeClientOptions> configure)
        => AddTypeSafeClient(services, configuration: null, configure);

    /// <summary>Applies the <c>TypeSafe</c> section onto <paramref name="options"/> with explicit parsing.
    /// Precedence-wise this runs after environment aliases and before the registration callback.</summary>
    internal static void ApplyConfigurationSection(TypeSafeClientOptions options, IConfigurationSection section)
    {
        if (section["DefaultModel"] is { Length: > 0 } defaultModel)
            options.DefaultModel = defaultModel;
        if (section["ApiKey"] is { Length: > 0 } apiKey)
            options.ApiKey = apiKey;
        if (section["BaseUrl"] is { Length: > 0 } baseUrl)
            options.BaseUrl = baseUrl;
        if (bool.TryParse(section["AllowInsecureHttp"], out var allowInsecureHttp))
            options.AllowInsecureHttp = allowInsecureHttp;
        if (int.TryParse(section["MaxResponseBytes"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxResponseBytes))
            options.MaxResponseBytes = maxResponseBytes;

        var retry = section.GetSection("Retry");
        if (int.TryParse(retry["MaxRetries"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxRetries))
            options.Retry.MaxRetries = maxRetries;
        if (TimeSpan.TryParse(retry["BackoffInitial"], CultureInfo.InvariantCulture, out var backoffInitial))
            options.Retry.BackoffInitial = backoffInitial;
        if (TimeSpan.TryParse(retry["BackoffMax"], CultureInfo.InvariantCulture, out var backoffMax))
            options.Retry.BackoffMax = backoffMax;
        if (bool.TryParse(retry["RespectRetryAfter"], out var respectRetryAfter))
            options.Retry.RespectRetryAfter = respectRetryAfter;
        if (bool.TryParse(retry["UseJitter"], out var useJitter))
            options.Retry.UseJitter = useJitter;

        if (retry["PerAttemptTimeout"] is { } perAttempt)
            options.Retry.PerAttemptTimeout = perAttempt.Length == 0 ? null : TimeSpan.Parse(perAttempt, CultureInfo.InvariantCulture);
        if (retry["TotalTimeoutBudget"] is { } budget)
            options.Retry.TotalTimeoutBudget = budget.Length == 0 ? null : TimeSpan.Parse(budget, CultureInfo.InvariantCulture);
    }
}
