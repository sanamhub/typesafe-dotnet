using System.Collections.Generic;
using System.Net;

namespace TypeSafeSharp;

// What every TypeSafeApiException carries, in one object so the internal constructors stay short.
internal sealed class ApiErrorDetails
{
    public ApiErrorDetails(
        string message,
        HttpStatusCode statusCode,
        string endpoint,
        string? requestId,
        string? body,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
    {
        Message = message;
        StatusCode = statusCode;
        Endpoint = endpoint;
        RequestId = requestId;
        Body = body;
        Headers = headers;
    }

    public string Message { get; }

    public HttpStatusCode StatusCode { get; }

    public string Endpoint { get; }

    public string? RequestId { get; }

    public string? Body { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
}
