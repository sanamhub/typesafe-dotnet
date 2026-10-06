using System.Collections.Generic;

namespace TypeSafeSharp;

/// <summary>One field the server rejected in an HTTP 422 response.</summary>
public sealed class ValidationError
{
    internal ValidationError(IReadOnlyList<string> location, string message, string type)
    {
        Location = location;
        Message = message;
        Type = type;
    }

    /// <summary>
    /// Path to the rejected field, for example <c>questions</c>, <c>urgency</c>, <c>criteria</c>.
    /// The request-body prefix the server adds is already removed.
    /// </summary>
    public IReadOnlyList<string> Location { get; }

    /// <summary>The server's description of the problem.</summary>
    public string Message { get; }

    /// <summary>The server's machine-readable error type, for example <c>too_short</c>.</summary>
    public string Type { get; }
}
