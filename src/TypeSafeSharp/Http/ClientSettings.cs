using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TypeSafeSharp;

// The immutable snapshot the client runs on. Built once from the caller's options, which are
// never read again or written to (ADR-0007), so changing them later cannot change a live client.
internal sealed class ClientSettings
{
    public const string BaseUrlVariable = "TYPESAFE_BASE_URL";
    public const string DefaultModelVariable = "TYPESAFE_DEFAULT_MODEL";
    private static readonly Uri s_defaultBaseUrl = new("https://api.typesafe.ai");

    private ClientSettings(
        string apiKey,
        Uri baseUrl,
        string defaultModel,
        TimeSpan attemptTimeout,
        TimeSpan totalTimeout,
        RetrySettings retry,
        ILoggerFactory loggerFactory)
    {
        ApiKey = apiKey;
        BaseUrl = baseUrl;
        DefaultModel = defaultModel;
        AttemptTimeout = attemptTimeout;
        TotalTimeout = totalTimeout;
        Retry = retry;
        LoggerFactory = loggerFactory;
    }

    public string ApiKey { get; }

    // No trailing slash, so callers append "/v1/..." and a gateway's base path survives.
    public Uri BaseUrl { get; }

    public string DefaultModel { get; }

    public TimeSpan AttemptTimeout { get; }

    public TimeSpan TotalTimeout { get; }

    public RetrySettings Retry { get; }

    public ILoggerFactory LoggerFactory { get; }

    public static ClientSettings From(TypeSafeClientOptions options)
    {
        Guard.NotNull(options);
        return new ClientSettings(
            TypeSafeSharp.ApiKey.Resolve(options.ApiKey),
            ResolveBaseUrl(options.BaseUrl),
            NonBlank(options.DefaultModel) ?? NonBlank(Environment.GetEnvironmentVariable(DefaultModelVariable)) ?? "jev-latest",
            Guard.Positive(options.AttemptTimeout, nameof(options.AttemptTimeout)),
            Guard.Positive(options.TotalTimeout, nameof(options.TotalTimeout)),
            RetrySettings.From(options.Retry),
            options.LoggerFactory ?? NullLoggerFactory.Instance);
    }

    private static Uri ResolveBaseUrl(Uri? fromOptions)
    {
        var url = fromOptions;
        if (url is null)
        {
            var fromEnvironment = NonBlank(Environment.GetEnvironmentVariable(BaseUrlVariable));
            if (fromEnvironment is null)
            {
                return s_defaultBaseUrl;
            }

            if (!Uri.TryCreate(fromEnvironment, UriKind.Absolute, out url))
            {
                throw new TypeSafeConfigurationException("TYPESAFE_BASE_URL is not an absolute URL.");
            }
        }

        // Messages name the rule, not the URL: a base URL can carry credentials or tokens.
        if (!url.IsAbsoluteUri)
        {
            throw new TypeSafeConfigurationException("The base URL must be absolute.");
        }

        if (url.UserInfo.Length > 0)
        {
            throw new TypeSafeConfigurationException("The base URL must not contain user info. Use TypeSafeClientOptions.ApiKey.");
        }

        if (url.Query.Length > 0 || url.Fragment.Length > 0)
        {
            throw new TypeSafeConfigurationException("The base URL must not have a query string or fragment.");
        }

        var secure = url.Scheme == Uri.UriSchemeHttps;
        if (!secure && !(url.Scheme == Uri.UriSchemeHttp && url.IsLoopback))
        {
            throw new TypeSafeConfigurationException("The base URL must use https. Plain http is allowed only to a loopback address such as localhost.");
        }

        return new Uri(url.GetLeftPart(UriPartial.Path).TrimEnd('/'));
    }

    private static string? NonBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
