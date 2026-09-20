using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class TypeSafeClientOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BlankApiKey_IsRejected(string apiKey)
    {
        var options = new TypeSafeClientOptions { ApiKey = apiKey };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void RelativeBaseUrl_IsRejected()
    {
        var options = new TypeSafeClientOptions { ApiKey = "k", BaseUrl = "api.typesafe.ai/" };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Theory]
    [InlineData("https://api.typesafe.ai/?x=1")]
    [InlineData("https://api.typesafe.ai/#frag")]
    [InlineData("https://user:pass@api.typesafe.ai/")]
    [InlineData("https://api.typesafe.ai")]
    public void BaseUrl_WithQueryFragmentUserInfoOrMissingSlash_IsRejected(string baseUrl)
    {
        var options = new TypeSafeClientOptions { ApiKey = "k", BaseUrl = baseUrl };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void PlainHttp_IsRejectedUnlessExplicitlyAllowed()
    {
        var rejected = new TypeSafeClientOptions { ApiKey = "k", BaseUrl = "http://localhost:8080/" };
        Assert.Throws<ArgumentException>(() => rejected.Validate());

        var allowed = new TypeSafeClientOptions { ApiKey = "k", BaseUrl = "http://localhost:8080/", AllowInsecureHttp = true };
        allowed.Validate();
    }

    [Fact]
    public void InvalidRetrySettings_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypeSafeClientOptions { ApiKey = "k", Retry = { MaxRetries = -1 } }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypeSafeClientOptions { ApiKey = "k", Retry = { BackoffInitial = TimeSpan.Zero } }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypeSafeClientOptions { ApiKey = "k", Retry = { BackoffMax = TimeSpan.Zero } }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypeSafeClientOptions { ApiKey = "k", Retry = { PerAttemptTimeout = TimeSpan.Zero } }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypeSafeClientOptions { ApiKey = "k", Retry = { TotalTimeoutBudget = TimeSpan.Zero } }.Validate());
    }

    [Fact]
    public void NullableBudgets_MayBeDisabled()
    {
        var options = new TypeSafeClientOptions
        {
            ApiKey = "k",
            Retry = { PerAttemptTimeout = null, TotalTimeoutBudget = null }
        };

        options.Validate();
    }

    [Fact]
    public void EnvironmentVariables_AreApplied()
    {
        try
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "env-key");
            Environment.SetEnvironmentVariable("TYPESAFE_BASE_URL", "https://env.example.com/");
            Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", "jev-env");

            var options = TypeSafeClientOptions.FromEnvironment();

            Assert.Equal("env-key", options.ApiKey);
            Assert.Equal("https://env.example.com/", options.BaseUrl);
            Assert.Equal("jev-env", options.DefaultModel);
            options.Validate();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", null);
            Environment.SetEnvironmentVariable("TYPESAFE_BASE_URL", null);
            Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", null);
        }
    }

    [Fact]
    public void DiPrecedence_EnvironmentThenConfigurationThenCallback()
    {
        try
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "sk-env");
            Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", "jev-env");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection([new KeyValuePair<string, string?>("TypeSafe:DefaultModel", "jev-config")])
                .Build();

            var withoutCallback = ResolveModel(configuration, configure: null);
            Assert.Equal("jev-config", withoutCallback);

            var withCallback = ResolveModel(configuration, o => o.DefaultModel = "jev-callback");
            Assert.Equal("jev-callback", withCallback);

            var plainServices = new ServiceCollection();
            plainServices.AddTypeSafeClient();
            using var plain = plainServices.BuildServiceProvider();
            Assert.Equal("jev-env", plain.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value.DefaultModel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", null);
            Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", null);
        }
    }

    [Fact]
    public void RetryProperty_CanBeAssigned()
    {
        var customRetry = new TypeSafeRetryOptions { MaxRetries = 5, UseJitter = false };
        var options = new TypeSafeClientOptions { ApiKey = "sk-test", Retry = customRetry };

        Assert.Same(customRetry, options.Retry);
        Assert.Equal(5, options.Retry.MaxRetries);
        Assert.False(options.Retry.UseJitter);
    }

    [Fact]
    public void NullRetryProperty_IsRejected()
    {
        var options = new TypeSafeClientOptions { ApiKey = "sk-test", Retry = null! };
        Assert.Throws<ArgumentNullException>(() => options.Validate());
    }

    [Fact]
    public void SettingsToString_RedactsApiKey()
    {
        var options = new TypeSafeClientOptions { ApiKey = "super-secret-key-12345" };
        var settings = TypeSafeClientSettings.Create(options);

        var str = settings.ToString();
        Assert.DoesNotContain("super-secret-key-12345", str);
        Assert.Contains("[REDACTED]", str);
    }

    private static string ResolveModel(IConfiguration configuration, Action<TypeSafeClientOptions>? configure)
    {
        var services = new ServiceCollection();
        services.AddTypeSafeClient(configuration, configure);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value.DefaultModel;
    }
}
