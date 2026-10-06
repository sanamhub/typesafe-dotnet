using System;

namespace TypeSafeSharp;

/// <summary>The server returned HTTP 400: the request was malformed.</summary>
public sealed class TypeSafeBadRequestException : TypeSafeApiException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeBadRequestException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeBadRequestException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeBadRequestException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafeBadRequestException(ApiErrorDetails details) : base(details) { }
}
