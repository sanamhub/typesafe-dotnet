using System;
using System.Collections.Generic;

namespace TypeSafeSharp;

// The one place that maps a status to an exception type, shared by the model factory and the
// error mapper (T08) so the two cannot disagree.
internal static class ErrorTypes
{
    public static TypeSafeApiException Create(ApiErrorDetails details, TimeSpan? retryAfter, IReadOnlyList<ValidationError> validationErrors)
        => (int)details.StatusCode switch
        {
            400 => new TypeSafeBadRequestException(details),
            401 => new TypeSafeAuthenticationException(details),
            403 => new TypeSafePermissionDeniedException(details),
            404 => new TypeSafeNotFoundException(details),
            422 => new TypeSafeUnprocessableEntityException(details, validationErrors),
            429 => new TypeSafeRateLimitException(details, retryAfter),
            529 => new TypeSafeOverloadedException(details),
            >= 500 and <= 599 => new TypeSafeInternalServerException(details),
            _ => new TypeSafeApiException(details),
        };
}
