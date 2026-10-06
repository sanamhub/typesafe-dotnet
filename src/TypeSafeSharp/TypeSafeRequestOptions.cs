using System;

namespace TypeSafeSharp;

/// <summary>Overrides for one call. A null property uses the client's value. Checked when the call starts.</summary>
public sealed class TypeSafeRequestOptions
{
    /// <summary>The limit for one HTTP attempt in this call. Must be positive and finite.</summary>
    public TimeSpan? AttemptTimeout { get; set; }

    /// <summary>The limit for this whole call, retries included. Must be positive and finite.</summary>
    public TimeSpan? TotalTimeout { get; set; }

    /// <summary>The retry policy for this call.</summary>
    public RetryPolicy? Retry { get; set; }
}
