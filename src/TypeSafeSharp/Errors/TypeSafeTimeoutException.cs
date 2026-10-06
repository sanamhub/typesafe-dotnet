using System;

namespace TypeSafeSharp;

/// <summary>An attempt timed out, or the retries used up the total time budget.</summary>
public sealed class TypeSafeTimeoutException : TypeSafeConnectionException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeTimeoutException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeTimeoutException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeTimeoutException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafeTimeoutException(string message, TimeSpan timeout, Exception? innerException)
        : base(message, innerException!) => Timeout = timeout;

    /// <summary>The limit that ran out: the attempt timeout or the total budget.</summary>
    public TimeSpan Timeout { get; }
}
