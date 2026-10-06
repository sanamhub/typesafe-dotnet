using System;

namespace TypeSafeSharp;

/// <summary>
/// The client cannot send a request: the API key is missing or malformed, or the base URL is not
/// an absolute http or https URL. The message names the setting, never its value.
/// </summary>
public sealed class TypeSafeConfigurationException : TypeSafeException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeConfigurationException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeConfigurationException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}
