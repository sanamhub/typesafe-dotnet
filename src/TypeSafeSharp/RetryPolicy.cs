namespace TypeSafeSharp;

/// <summary>
/// How many times a failed attempt is retried. Statuses 408, 429 and 5xx and connection errors
/// are retried, with backoff from 500 ms doubling to 5 s and the server's <c>Retry-After</c>
/// honoured up to 60 s, matching the official SDKs. Only the two settings below are options.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>A new policy with the defaults: 2 retries, timeouts retried. A new instance on every read.</summary>
    public static RetryPolicy Default => new();

    /// <summary>A new policy with no retries. A new instance on every read.</summary>
    public static RetryPolicy None => new() { MaxRetries = 0 };

    /// <summary>Retries after the first attempt. Default 2. Must not be negative; checked when the client is built or the call starts.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Whether an attempt that timed out is retried. Default true. A timed-out request may still
    /// have been processed and billed, so set false where a second charge matters more than an answer.
    /// </summary>
    public bool RetryOnTimeout { get; set; } = true;
}
