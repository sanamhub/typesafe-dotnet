using System;
using System.Collections.Generic;
using System.Net;

namespace TypeSafeSharp;

/// <summary>
/// The server returned a non-2xx status after retries were used up. Subclasses cover the statuses
/// callers usually handle differently; any other status is this type.
/// </summary>
public class TypeSafeApiException : TypeSafeException
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> s_noHeaders =
        new Dictionary<string, IReadOnlyList<string>>(0, StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeApiException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeApiException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeApiException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafeApiException(ApiErrorDetails details)
        : base(details.Message)
    {
        StatusCode = details.StatusCode;
        Endpoint = details.Endpoint;
        RequestId = details.RequestId;
        Body = details.Body;
        Headers = details.Headers;
    }

    /// <summary>The HTTP status the server returned.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The method and path that failed, for example <c>POST /v1/systemone</c>.</summary>
    public string Endpoint { get; } = string.Empty;

    /// <summary>
    /// The <c>x-typesafe-request-id</c> response header, or null when the server sent none.
    /// Quote it when asking TypeSafe about a failed request.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// The raw response body. It can contain parts of the request, which may be customer data, so
    /// do not log it.
    /// </summary>
    public string? Body { get; }

    /// <summary>The response headers, compared case-insensitively. Empty when none were recorded.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; } = s_noHeaders;
}
