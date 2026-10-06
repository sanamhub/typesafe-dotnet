using System;

namespace TypeSafeSharp;

// Random.Shared is not on netstandard2.0, and System.Random is not thread-safe.
internal static class Jitter
{
    private static readonly object s_gate = new();
#pragma warning disable CA5394 // Backoff jitter spreads retries; it is not a security decision.
    private static readonly Random s_source = new();

    public static double Next()
    {
        lock (s_gate)
        {
            return s_source.NextDouble();
        }
    }
#pragma warning restore CA5394
}
