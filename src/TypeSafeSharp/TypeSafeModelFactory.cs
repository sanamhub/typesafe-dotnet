using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace TypeSafeSharp;

/// <summary>
/// Builds SDK result and exception objects for callers' unit tests, since their constructors are
/// internal. The SDK itself never calls this class.
/// </summary>
public static class TypeSafeModelFactory
{
    private const string SystemOneEndpoint = "POST /v1/systemone";

    /// <summary>
    /// Creates the exception the client throws for <paramref name="statusCode"/>: the
    /// <see cref="TypeSafeApiException"/> subclass for 400, 401, 403, 404, 422, 429, 529 and other
    /// 5xx statuses, or the base type for anything else. <see cref="TypeSafeApiException.Endpoint"/>
    /// is <c>POST /v1/systemone</c>.
    /// </summary>
    /// <param name="statusCode">The HTTP status, for example 429.</param>
    /// <param name="message">The message, or null for <c>"&lt;status&gt; status code (no message in body)"</c>.</param>
    /// <param name="requestId">The request id, or null.</param>
    /// <param name="retryAfter">Sets <see cref="TypeSafeRateLimitException.RetryAfter"/>; ignored for other statuses.</param>
    /// <returns>The exception, not thrown.</returns>
    public static TypeSafeApiException ApiException(int statusCode, string? message = null, string? requestId = null, TimeSpan? retryAfter = null)
    {
        var details = new ApiErrorDetails(
            message ?? statusCode.ToString(CultureInfo.InvariantCulture) + " status code (no message in body)",
            (HttpStatusCode)statusCode,
            SystemOneEndpoint,
            requestId,
            body: null,
            new Dictionary<string, IReadOnlyList<string>>(0, StringComparer.OrdinalIgnoreCase));
        return ErrorTypes.Create(details, retryAfter, Array.Empty<ValidationError>());
    }
}
