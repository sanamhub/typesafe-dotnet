using System;

namespace TypeSafeSharp;

/// <summary>The server returned HTTP 403: the key is valid but may not use this model or endpoint.</summary>
public sealed class TypeSafePermissionDeniedException : TypeSafeApiException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafePermissionDeniedException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafePermissionDeniedException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafePermissionDeniedException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafePermissionDeniedException(ApiErrorDetails details) : base(details) { }
}
