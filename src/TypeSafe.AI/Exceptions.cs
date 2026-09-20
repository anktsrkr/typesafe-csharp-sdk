using System.Net;
namespace TypeSafe.AI;

public class TypeSafeException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
public sealed class TypeSafeAuthenticationException() : TypeSafeException("TypeSafe authentication failed.", HttpStatusCode.Unauthorized);
public sealed class TypeSafeValidationException() : TypeSafeException("TypeSafe rejected the request.", HttpStatusCode.UnprocessableEntity);
public sealed class TypeSafeRateLimitException() : TypeSafeException("TypeSafe rate limit exceeded.", HttpStatusCode.TooManyRequests);
public sealed class TypeSafeOverloadedException() : TypeSafeException("TypeSafe is temporarily overloaded.", (HttpStatusCode)529);
public sealed class TypeSafeConnectionException(string message, Exception? innerException = null) : TypeSafeException(message, innerException: innerException);
public sealed class TypeSafeTimeoutException(string message = "TypeSafe evaluation timed out.", Exception? innerException = null) : TypeSafeException(message, innerException: innerException);
public sealed class TypeSafeProtocolException(string message, Exception? innerException = null) : TypeSafeException(message, innerException: innerException);
