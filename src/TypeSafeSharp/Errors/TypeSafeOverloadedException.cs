using System;

namespace TypeSafeSharp;

/// <summary>The server returned HTTP 529: TypeSafe is overloaded. Retried like any 5xx, but a separate type so callers can tell overload from a server bug.</summary>
public sealed class TypeSafeOverloadedException : TypeSafeInternalServerException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeOverloadedException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeOverloadedException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeOverloadedException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafeOverloadedException(ApiErrorDetails details) : base(details) { }
}
