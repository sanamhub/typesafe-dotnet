using System;
using System.Collections.Generic;

namespace TypeSafeSharp;

/// <summary>The server returned HTTP 422: the request was well formed but failed the API's validation.</summary>
public sealed class TypeSafeUnprocessableEntityException : TypeSafeApiException
{
    /// <summary>Creates an exception with a default message.</summary>
    public TypeSafeUnprocessableEntityException() { }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public TypeSafeUnprocessableEntityException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TypeSafeUnprocessableEntityException(string message, Exception innerException) : base(message, innerException) { }

    internal TypeSafeUnprocessableEntityException(ApiErrorDetails details, IReadOnlyList<ValidationError> validationErrors)
        : base(details) => ValidationErrors = validationErrors;

    /// <summary>One entry per field the server rejected. Empty when the body listed none.</summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; } = Array.Empty<ValidationError>();
}
