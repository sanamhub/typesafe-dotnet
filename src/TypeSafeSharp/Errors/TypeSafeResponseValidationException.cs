using System;

namespace TypeSafeSharp;

/// <summary>
/// The server returned 2xx with a body the SDK cannot use: not JSON, a missing field, or a value of
/// the wrong type. Usually a proxy or gateway page, or an API change.
/// </summary>
public sealed class TypeSafeResponseValidationException : TypeSafeException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeResponseValidationException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeResponseValidationException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeResponseValidationException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafeResponseValidationException(string jsonPath, string problem)
        : base($"The response did not match the API contract at {jsonPath}: {problem}.") => JsonPath = jsonPath;

    /// <summary>Where the body broke the contract: <c>$</c> for the whole body, otherwise the field path.</summary>
    public string? JsonPath { get; }
}
