using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace TypeSafe.AI;

/// <summary>Evaluation activity source. Tag values are model/usage metadata only — never state,
/// question text, credentials, or raw provider errors.</summary>
internal static class TypeSafeDiagnostics
{
    public const string SourceName = "TypeSafe.AI";

    public static readonly ActivitySource Source = new(SourceName, "0.1.0");
}

/// <summary>The default TypeSafe System One client: one request sent through the Polly resilience
/// handler chain, with an operation-wide deadline covering body reads and parsing.</summary>
public sealed class TypeSafeClient : ITypeSafeClient, IDisposable
{
    /// <summary>The named HttpClient used by the DI registration.</summary>
    public const string HttpClientName = "TypeSafe";

    /// <summary>Request path appended relatively to the configured base URL.</summary>
    internal const string RequestPath = "v1/systemone";

    private readonly HttpClient _client;
    private readonly TypeSafeClientOptions _options;
    private readonly Uri _endpoint;
    private readonly bool _ownsClient;

    /// <summary>Creates a self-contained client that owns its HTTP handler chain, including the
    /// Polly resilience pipeline built from <paramref name="options"/> and the Bearer credential.</summary>
    public TypeSafeClient(TypeSafeClientOptions options)
        : this(CreateDefaultClient(options), options, ownsClient: true)
    {
    }

    /// <summary>Creates a client over a supplied HttpClient, typically an IHttpClientFactory-created
    /// one. The handler chain is expected to carry the resilience pipeline and the credential
    /// (see <see cref="TypeSafeServiceCollectionExtensions.AddTypeSafeClient"/>).</summary>
    public TypeSafeClient(HttpClient client, TypeSafeClientOptions options)
        : this(client, options, ownsClient: false)
    {
    }

    private TypeSafeClient(HttpClient client, TypeSafeClientOptions options, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _client = client;
        _options = options;
        _endpoint = new Uri(new Uri(options.BaseUrl), RequestPath);
        _ownsClient = ownsClient;
    }

    /// <inheritdoc />
    public async Task<SystemOneResponse> SystemOneAsync(TypeSafeContent state,
        IReadOnlyDictionary<string, TypeSafeQuestion> questions, string? model = null,
        CancellationToken cancellationToken = default)
    {
        // Local validation happens before any network I/O; the request is immutable afterwards.
        var request = SystemOneRequest.Create(state, questions, model ?? _options.DefaultModel);

        using var activity = TypeSafeDiagnostics.Source.StartActivity("TypeSafe.SystemOne", ActivityKind.Client);
        activity?.SetTag("typesafe.model", request.Model);
        try
        {
            var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            activity?.SetTag("typesafe.usage.input_tokens", response.Usage.InputTokens);
            activity?.SetTag("typesafe.usage.output_tokens", response.Usage.OutputTokens);
            if (response.RequestId is not null)
                activity?.SetTag("typesafe.request_id", response.RequestId);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception exception)
        {
            // Status description is the exception type name only; never provider bodies or user text.
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
    }

    private async Task<SystemOneResponse> SendAsync(SystemOneRequest request, CancellationToken callerToken)
    {
        // HttpClient does not pre-check the token; fail fast before any network I/O.
        callerToken.ThrowIfCancellationRequested();

        using var deadline = StartDeadline(callerToken);
        var token = deadline?.Token ?? callerToken;
        try
        {
            // PostAsJsonAsync serializes through the source-generated contract; the buffered
            // content is deterministic, so handler-level retries replay identical bytes.
            using var response = await _client.PostAsJsonAsync(_endpoint, request,
                TypeSafeJsonContext.Default.SystemOneRequest, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw MapFailure(response);

            var requestId = response.Headers.TryGetValues("x-typesafe-request-id", out var values)
                ? values.FirstOrDefault()
                : null;
            var body = await ReadBoundedAsync(response.Content, _options.MaxResponseBytes, token).ConfigureAwait(false);
            return TypeSafeResponseDecoder.DecodeSystemOne(body, requestId);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new TypeSafeTimeoutException("The TypeSafe evaluation did not complete within the configured budget.", exception);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new TypeSafeTimeoutException("The TypeSafe evaluation did not complete within the configured budget.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new TypeSafeConnectionException("The TypeSafe request failed before a response was received.", exception);
        }
    }

    private CancellationTokenSource? StartDeadline(CancellationToken callerToken)
    {
        if (_options.Retry.TotalTimeoutBudget is not { } budget)
            return null;
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        deadline.CancelAfter(budget);
        return deadline;
    }

    private static Exception MapFailure(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        return status switch
        {
            401 => new TypeSafeAuthenticationException(),
            422 => new TypeSafeValidationException(),
            429 => new TypeSafeRateLimitException(),
            529 => new TypeSafeOverloadedException(),
            _ => new TypeSafeException($"TypeSafe returned the unexpected status code {status}.", response.StatusCode)
        };
    }

    private static async Task<string> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken token)
    {
        // The framework has no bounded read; responses must not be trusted to honor size limits.
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[8192];
        using var memory = new MemoryStream();
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > maxBytes)
                throw new TypeSafeProtocolException($"The TypeSafe response exceeds the configured limit of {maxBytes} bytes.");
            memory.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static HttpClient CreateDefaultClient(TypeSafeClientOptions options)
    {
        var pipelineBuilder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        TypeSafeResiliencePipeline.Configure(pipelineBuilder, options);
        var handler = new ResilienceHandler(pipelineBuilder.Build())
        {
            InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false }
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        return client;
    }

    /// <summary>Disposes the self-created handler chain; supplied clients stay owned by their factory.</summary>
    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
