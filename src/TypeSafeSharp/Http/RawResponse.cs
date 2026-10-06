using System.Collections.Generic;

namespace TypeSafeSharp;

// A buffered 2xx response, detached from HttpResponseMessage so nothing needs disposing later.
internal sealed class RawResponse
{
    public RawResponse(int statusCode, byte[] body, string? requestId, IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
    {
        StatusCode = statusCode;
        Body = body;
        RequestId = requestId;
        Headers = headers;
    }

    public int StatusCode { get; }

    public byte[] Body { get; }

    public string? RequestId { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
}
