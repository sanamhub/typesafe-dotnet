using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace TypeSafeSharp;

// Ported from typesafe-sdk-js v0.6.0 src/errors.ts (extractMessage, describeValidationErrors),
// minus raw JSON echo: a body we cannot read a message from may hold customer state.
internal static class ErrorMessage
{
    private const int MaxRawText = 200;

    public static string Describe(int status, string? body)
    {
        var prefix = status.ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(body))
        {
            return prefix + " status code (no body)";
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body!);
        }
        catch (JsonException)
        {
            // Plain text, usually from a proxy. Short and capped.
            var text = body!.Length > MaxRawText ? body.Substring(0, MaxRawText) + "..." : body;
            return prefix + " " + text;
        }

        using (document)
        {
            var detail = Extract(document.RootElement);
            return detail is null ? prefix + " status code (no message in body)" : prefix + " " + detail;
        }
    }

    // FastAPI loc segments as strings, with the "body" prefix the server adds dropped.
    public static IReadOnlyList<string> Location(JsonElement item)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("loc", out var loc) && loc.ValueKind == JsonValueKind.Array
            ? loc.EnumerateArray()
                .Select(segment => segment.ValueKind == JsonValueKind.String ? segment.GetString()! : segment.GetRawText())
                .Where(segment => segment != "body")
                .ToList()
                .AsReadOnly()
            : Array.Empty<string>();

    public static string? StringProperty(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static string? Extract(JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.String)
        {
            return NullIfEmpty(body.GetString());
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (body.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }

            if (StringProperty(error, "message") is { } nested)
            {
                return nested;
            }
        }

        if (StringProperty(body, "message") is { } message)
        {
            return message;
        }

        if (body.TryGetProperty("detail", out var detail))
        {
            if (detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }

            if (StringProperty(detail, "message") is { } nested)
            {
                return nested;
            }

            if (detail.ValueKind == JsonValueKind.Array)
            {
                return DescribeValidationErrors(detail);
            }
        }

        return null;
    }

    // FastAPI detail items: "questions.urgency.score.criteria: List should have at least 1 item".
    // Only loc and msg are read; input and ctx can echo the request, so they never reach a message.
    private static string? DescribeValidationErrors(JsonElement errors)
    {
        var parts = new List<string>();
        foreach (var item in errors.EnumerateArray())
        {
            if (StringProperty(item, "msg") is not { } msg)
            {
                continue;
            }

            var location = string.Join(".", Location(item));
            parts.Add(location.Length > 0 ? location + ": " + msg : msg);
        }

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
