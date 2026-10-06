using System;

namespace TypeSafeSharp;

/// <summary>The server returned HTTP 404: usually an unknown model or a wrong base URL.</summary>
public sealed class TypeSafeNotFoundException : TypeSafeApiException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeNotFoundException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeNotFoundException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeNotFoundException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafeNotFoundException(ApiErrorDetails details) : base(details) { }
}
