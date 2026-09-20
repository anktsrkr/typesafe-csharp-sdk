using System.Net;

namespace TypeSafe.AI;

/// <summary>SDK failure. Provider bodies are available explicitly, never included in the message.</summary>
public class TypeSafeException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null,
    string? requestId = null, string? responseBody = null, string? retryAfter = null)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public string? RequestId { get; } = requestId;
    public string? ResponseBody { get; } = responseBody;
    /// <summary>The final response's unmodified Retry-After header, when present.</summary>
    public string? RetryAfter { get; } = retryAfter;
}

public sealed class TypeSafeAuthenticationException(string? requestId = null, string? responseBody = null, string? retryAfter = null)
    : TypeSafeException("TypeSafe authentication failed.", HttpStatusCode.Unauthorized,
        requestId: requestId, responseBody: responseBody, retryAfter: retryAfter);
public sealed class TypeSafeValidationException(string? requestId = null, string? responseBody = null, string? retryAfter = null)
    : TypeSafeException("TypeSafe rejected the request.", HttpStatusCode.UnprocessableEntity,
        requestId: requestId, responseBody: responseBody, retryAfter: retryAfter);
public sealed class TypeSafeRateLimitException(string? requestId = null, string? responseBody = null, string? retryAfter = null)
    : TypeSafeException("TypeSafe rate limit exceeded.", HttpStatusCode.TooManyRequests,
        requestId: requestId, responseBody: responseBody, retryAfter: retryAfter);
public sealed class TypeSafeOverloadedException(string? requestId = null, string? responseBody = null, string? retryAfter = null)
    : TypeSafeException("TypeSafe is temporarily overloaded.", (HttpStatusCode)529,
        requestId: requestId, responseBody: responseBody, retryAfter: retryAfter);
public sealed class TypeSafeConnectionException(string message, Exception? innerException = null)
    : TypeSafeException(message, innerException: innerException);
public sealed class TypeSafeTimeoutException(string message = "TypeSafe evaluation timed out.", Exception? innerException = null)
    : TypeSafeException(message, innerException: innerException);
public sealed class TypeSafeProtocolException(string message, Exception? innerException = null)
    : TypeSafeException(message, innerException: innerException);
