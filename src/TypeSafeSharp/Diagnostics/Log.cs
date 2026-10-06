using Microsoft.Extensions.Logging;

namespace TypeSafeSharp;

// Event ids are part of the contract (ADR-0010). Never add a body, a header or the key as a parameter.
internal static partial class Log
{
    public const string Category = "TypeSafeSharp";

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "{Endpoint} attempt {Attempt} started")]
    public static partial void AttemptStarted(ILogger logger, string endpoint, int attempt);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{Endpoint} {StatusCode} in {ElapsedMs} ms, request {RequestId}")]
    public static partial void AttemptCompleted(ILogger logger, string endpoint, int statusCode, long elapsedMs, string? requestId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Retrying {Endpoint} after {Reason}, retry {Retry} of {MaxRetries}, waiting {DelayMs} ms")]
    public static partial void Retrying(ILogger logger, string endpoint, string reason, int retry, int maxRetries, long delayMs);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Unknown answer kind {Kind} for question {QuestionId}; returned as UnknownAnswer")]
    public static partial void UnknownAnswerKind(ILogger logger, string kind, string questionId);

    // Warning, not Error: the caller decides whether a failed call is an error.
    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "{Endpoint} failed after {Attempts} attempts: {StatusCode}, request {RequestId}")]
    public static partial void CallFailed(ILogger logger, string endpoint, int attempts, string statusCode, string? requestId);
}
