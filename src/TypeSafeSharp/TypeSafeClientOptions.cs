using System;
using Microsoft.Extensions.Logging;

namespace TypeSafeSharp;

/// <summary>
/// Settings for a <c>TypeSafeClient</c>. The client copies them when it is built, so changing
/// this object afterwards does not affect a client that already exists. Values are checked
/// then, not when they are set.
/// </summary>
public sealed class TypeSafeClientOptions
{
    /// <summary>
    /// The API key. Null reads the <c>TYPESAFE_API_KEY</c> environment variable. An empty or
    /// malformed key throws <see cref="TypeSafeConfigurationException"/> when the client is built;
    /// it does not fall back to the environment.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The API root. Null reads <c>TYPESAFE_BASE_URL</c>, else <c>https://api.typesafe.ai</c>.
    /// Must be https, or http to a loopback address; a path is kept, so a gateway such as
    /// <c>https://openrouter.ai/api</c> works. Checked when the client is built.
    /// </summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>The model for requests that set none. Null or blank reads <c>TYPESAFE_DEFAULT_MODEL</c>, else <c>jev-latest</c>.</summary>
    public string? DefaultModel { get; set; }

    /// <summary>The limit for one HTTP attempt. Default 10 seconds. Must be positive and finite; checked when the client is built.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The limit for a whole call, retries and waits included. Default 30 seconds. Must be positive and finite; checked when the client is built.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How failed attempts are retried. Default: 2 retries, timeouts included.</summary>
    public RetryPolicy Retry { get; set; } = new RetryPolicy();

    /// <summary>Where the SDK logs retries and failures. Null logs nothing. Keys, headers and bodies are never logged.</summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>Describes the options with the API key masked as <c>***</c>.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
        => $"TypeSafeClientOptions {{ ApiKey = {(ApiKey is null ? "null" : "***")}, BaseUrl = {BaseUrl?.ToString() ?? "null"}, DefaultModel = {DefaultModel ?? "null"} }}";
}
