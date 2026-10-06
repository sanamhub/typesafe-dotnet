using System;

namespace TypeSafeSharp;

/// <summary>The server returned HTTP 401: the API key is missing, wrong or revoked.</summary>
public sealed class TypeSafeAuthenticationException : TypeSafeApiException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeAuthenticationException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeAuthenticationException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeAuthenticationException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafeAuthenticationException(ApiErrorDetails details) : base(details) { }
}
