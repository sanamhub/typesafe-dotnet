using System;

namespace TypeSafeSharp;

// What one call runs with: the client's settings, with any per-call option replacing its value
// whole. Checked here, when the call starts, with the same rules as the client options.
internal sealed class CallSettings
{
    private CallSettings(TimeSpan attemptTimeout, TimeSpan totalTimeout, RetrySettings retry, string endpoint, string? model)
    {
        AttemptTimeout = attemptTimeout;
        TotalTimeout = totalTimeout;
        Retry = retry;
        Endpoint = endpoint;
        Model = model;
    }

    public TimeSpan AttemptTimeout { get; }

    public TimeSpan TotalTimeout { get; }

    public RetrySettings Retry { get; }

    // "POST /v1/systemone", for exceptions and logs.
    public string Endpoint { get; }

    public string? Model { get; }

    public static CallSettings Resolve(ClientSettings settings, TypeSafeRequestOptions? options, string endpoint, string? model)
        => new(
            options?.AttemptTimeout is { } attempt ? Guard.Positive(attempt, nameof(TypeSafeRequestOptions.AttemptTimeout)) : settings.AttemptTimeout,
            options?.TotalTimeout is { } total ? Guard.Positive(total, nameof(TypeSafeRequestOptions.TotalTimeout)) : settings.TotalTimeout,
            options?.Retry is { } retry ? RetrySettings.From(retry) : settings.Retry,
            endpoint,
            model);
}
