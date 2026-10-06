using System;

namespace TypeSafeSharp;

// An immutable copy of a RetryPolicy plus the ADR-0005 constants, which are deliberately not
// options: they match the official SDKs, and a caller who needs other values adds their own
// resilience handler instead.
internal sealed class RetrySettings
{
    public static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(5);
    public const double Jitter = 0.25;
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

    private RetrySettings(int maxRetries, bool retryOnTimeout)
    {
        MaxRetries = maxRetries;
        RetryOnTimeout = retryOnTimeout;
    }

    public int MaxRetries { get; }

    public bool RetryOnTimeout { get; }

    public static RetrySettings From(RetryPolicy? policy)
    {
        policy ??= RetryPolicy.Default;
        if (policy.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy.MaxRetries, "RetryPolicy.MaxRetries cannot be negative.");
        }

        return new RetrySettings(policy.MaxRetries, policy.RetryOnTimeout);
    }

    public static bool IsRetryableStatus(int status) => status == 408 || status == 429 || (status >= 500 && status <= 599);
}
