using System;

namespace TypeSafeSharp;

/// <summary>
/// No HTTP response arrived: DNS, TLS, a reset connection, or an exception from a handler the
/// caller added to the <c>HttpClient</c> (for example a Polly timeout), kept as the inner exception.
/// </summary>
public class TypeSafeConnectionException : TypeSafeException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeConnectionException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeConnectionException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeConnectionException(string message, Exception innerException) : base(message, innerException) { }
}
