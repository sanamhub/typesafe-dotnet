using System;

namespace TypeSafeSharp;

/// <summary>The server returned HTTP 429 after retries were used up.</summary>
public sealed class TypeSafeRateLimitException : TypeSafeApiException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeRateLimitException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeRateLimitException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeRateLimitException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafeRateLimitException(ApiErrorDetails details, TimeSpan? retryAfter)
        : base(details) => RetryAfter = retryAfter;

    /// <summary>How long the server asked to wait, or null when it did not say.</summary>
    public TimeSpan? RetryAfter { get; }
}
