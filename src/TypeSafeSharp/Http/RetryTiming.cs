using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;

namespace TypeSafeSharp;

// Ported from typesafe-sdk-js v0.6.0 src/retry.ts (parseRetryAfter, retryDelayMs).
internal static class RetryTiming
{
    // Milliseconds as double so a huge header value cannot overflow TimeSpan.
    public static double? ParseRetryAfterMilliseconds(HttpResponseHeaders headers, DateTimeOffset now)
    {
        if (TryGetFirst(headers, "retry-after-ms", out var msText)
            && double.TryParse(msText, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            && ms >= 0 && !double.IsInfinity(ms))
        {
            return ms;
        }

        if (!TryGetFirst(headers, "Retry-After", out var raw))
        {
            return null;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds >= 0 && !double.IsInfinity(seconds) ? seconds * 1000 : null;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return Math.Max(0, (date - now).TotalMilliseconds);
        }

        return null;
    }

    // retryIndex is zero based: the first retry waits about InitialBackoff.
    public static TimeSpan Backoff(int retryIndex, TimeSpan initial, TimeSpan max, double jitter, double random01)
    {
        var exponential = Math.Min(initial.TotalMilliseconds * Math.Pow(2, retryIndex), max.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Round(exponential * (1 - (random01 * jitter)), MidpointRounding.AwayFromZero));
    }

    private static bool TryGetFirst(HttpResponseHeaders headers, string name, out string value)
    {
        if (headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            var first = values.FirstOrDefault();
            if (first is not null)
            {
                value = first.Trim();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
