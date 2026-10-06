using System;

namespace TypeSafeSharp;

/// <summary>The server returned HTTP 5xx after retries were used up.</summary>
public class TypeSafeInternalServerException : TypeSafeApiException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeInternalServerException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeInternalServerException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeInternalServerException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafeInternalServerException(ApiErrorDetails details) : base(details) { }
}
