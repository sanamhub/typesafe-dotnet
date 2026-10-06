using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TypeSafeSharp;

// Turns a final non-2xx response into its exception. headers holds response headers only, as
// the transport copies them, so Authorization can never end up on an exception.
internal static class ErrorMapper
{
    public static TypeSafeApiException Create(
        int status,
        byte[] body,
        string endpoint,
        string? requestId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        double? retryAfterMs)
    {
        var text = body.Length == 0 ? null : Encoding.UTF8.GetString(body);
        var details = new ApiErrorDetails(ErrorMessage.Describe(status, text), (HttpStatusCode)status, endpoint, requestId, text, headers);
        return ErrorTypes.Create(
            details,
            retryAfterMs is { } ms ? ToTimeSpan(ms) : null,
            status == 422 ? ValidationErrors(body) : Array.Empty<ValidationError>());
    }

    // TimeSpan.FromMilliseconds throws past TimeSpan.MaxValue; a hostile header must not crash the caller.
    private static TimeSpan ToTimeSpan(double milliseconds)
        => milliseconds >= TimeSpan.MaxValue.TotalMilliseconds ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(milliseconds);

    // Reads loc, msg and type only. input and ctx can echo the request and are never touched.
    private static IReadOnlyList<ValidationError> ValidationErrors(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("detail", out var detail)
                || detail.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ValidationError>();
            }

            var errors = new List<ValidationError>();
            foreach (var item in detail.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                errors.Add(new ValidationError(
                    ErrorMessage.Location(item),
                    ErrorMessage.StringProperty(item, "msg") ?? string.Empty,
                    ErrorMessage.StringProperty(item, "type") ?? string.Empty));
            }

            return errors.AsReadOnly();
        }
        catch (JsonException)
        {
            return Array.Empty<ValidationError>();
        }
    }
}
