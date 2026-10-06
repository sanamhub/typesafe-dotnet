using System;

namespace TypeSafeSharp;

/// <summary>
/// Base type of every error the SDK raises for the service or its configuration. The SDK never
/// throws this type itself, only its subclasses. Invalid arguments throw
/// <see cref="ArgumentException"/> instead, and caller cancellation throws
/// <see cref="OperationCanceledException"/>.
/// </summary>
public class TypeSafeException : Exception
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeException(string message, Exception innerException) : base(message, innerException) { }
}
