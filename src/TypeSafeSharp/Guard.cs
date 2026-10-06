using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TypeSafeSharp;

// One guard class for both targets, so argument checks need no #if (ADR-0001).
internal static class Guard
{
    public static T NotNull<T>([NotNull] T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class
        => value ?? throw new ArgumentNullException(name);

    // Timers take at most int.MaxValue ms. Timeout.InfiniteTimeSpan is -1 ms, so it fails the first test.
    public static TimeSpan Positive(TimeSpan value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => value > TimeSpan.Zero && value.TotalMilliseconds <= int.MaxValue
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "Must be greater than zero and at most 24.86 days.");
}
