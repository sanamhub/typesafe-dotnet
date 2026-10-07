using System;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TypeSafeSharp;

/// <summary>Registers <see cref="TypeSafeClient"/> in an <see cref="IServiceCollection"/>.</summary>
public static class TypeSafeServiceCollectionExtensions
{
    // The named HttpClient and the named options share this name.
    private const string Name = "TypeSafeSharp";

    private const string MissingKey =
        "No TypeSafe API key. Set TypeSafe:ApiKey in configuration or the TYPESAFE_API_KEY environment variable. Create a key at https://console.typesafe.ai/keys.";

    /// <summary>
    /// Registers a singleton <see cref="TypeSafeClient"/> configured from a configuration section,
    /// usually <c>builder.Configuration.GetSection("TypeSafe")</c>. Every option except
    /// <see cref="TypeSafeClientOptions.LoggerFactory"/> binds, including a nested <c>Retry</c>
    /// section. Keep the API key out of <c>appsettings.json</c>: use user secrets, a vault, or
    /// <c>TYPESAFE_API_KEY</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The section to bind.</param>
    /// <returns>The builder of the named <c>HttpClient</c> the client sends with, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <exception cref="OptionsValidationException">Thrown when the host starts, not here: no valid API key in the options or in <c>TYPESAFE_API_KEY</c>.</exception>
    public static IHttpClientBuilder AddTypeSafe(this IServiceCollection services, IConfiguration configuration)
    {
        Guard.NotNull(services);
        Guard.NotNull(configuration);
        return Register(services, services.AddOptions<TypeSafeClientOptions>(Name).Bind(configuration));
    }

    /// <summary>Registers a singleton <see cref="TypeSafeClient"/> configured in code.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the options. Leave <see cref="TypeSafeClientOptions.ApiKey"/> null to read <c>TYPESAFE_API_KEY</c>.</param>
    /// <returns>The builder of the named <c>HttpClient</c> the client sends with, for adding handlers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="OptionsValidationException">Thrown when the host starts, not here: no valid API key in the options or in <c>TYPESAFE_API_KEY</c>.</exception>
    public static IHttpClientBuilder AddTypeSafe(this IServiceCollection services, Action<TypeSafeClientOptions> configure)
    {
        Guard.NotNull(services);
        Guard.NotNull(configure);
        return Register(services, services.AddOptions<TypeSafeClientOptions>(Name).Configure(configure));
    }

    private static IHttpClientBuilder Register(IServiceCollection services, OptionsBuilder<TypeSafeClientOptions> options)
    {
        // At host start, not at Build(), so a missing or malformed key stops the app before the
        // first request. A custom validator keeps ApiKey.Validate's specific message (whitespace
        // inside, control character, non-ASCII), which never echoes the key (ADR-0008).
        services.AddSingleton<IValidateOptions<TypeSafeClientOptions>>(new ApiKeyValidator());
        options.ValidateOnStart();

        services.AddSingleton(provider =>
        {
            var monitored = provider.GetRequiredService<IOptionsMonitor<TypeSafeClientOptions>>().Get(Name);
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(Name);
            return new TypeSafeClient(httpClient, WithLogger(monitored, provider.GetService<ILoggerFactory>()));
        });

        // A singleton captures one HttpClient and never sees the factory's handler rotation
        // (ADR-0009), so the SDK handler rotates connections itself and the factory never retires it.
        return services.AddHttpClient(Name)
            .ConfigurePrimaryHttpMessageHandler(HttpDefaults.CreateHandler)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
    }

    // OptionsBuilder.Validate takes a fixed failure string, so it would collapse ApiKey.Validate's
    // specific message into MissingKey. A validator returns the message ApiKey.Validate produced.
    private sealed class ApiKeyValidator : IValidateOptions<TypeSafeClientOptions>
    {
        public ValidateOptionsResult Validate(string? name, TypeSafeClientOptions options)
        {
            // Registered for the options type, so it also sees options the app names itself.
            // Those never build this package's client, so their key is not this check's business.
            if (name != Name)
            {
                return ValidateOptionsResult.Skip;
            }

            // Same order as ApiKey.Resolve: a blank environment variable counts as no key, an
            // explicit empty option does not.
            var key = options.ApiKey;
            if (key is null)
            {
                var fromEnvironment = Environment.GetEnvironmentVariable(ApiKey.EnvironmentVariable);
                if (fromEnvironment is null || fromEnvironment.Trim().Length == 0)
                {
                    return ValidateOptionsResult.Fail(MissingKey);
                }

                key = fromEnvironment;
            }

            try
            {
                ApiKey.Validate(key);
                return ValidateOptionsResult.Success;
            }
            catch (TypeSafeConfigurationException ex)
            {
                return ValidateOptionsResult.Fail(ex.Message);
            }
        }
    }

    // A copy, so the container's logger never lands on the monitored options instance.
    private static TypeSafeClientOptions WithLogger(TypeSafeClientOptions options, ILoggerFactory? loggerFactory)
        => new()
        {
            ApiKey = options.ApiKey,
            BaseUrl = options.BaseUrl,
            DefaultModel = options.DefaultModel,
            AttemptTimeout = options.AttemptTimeout,
            TotalTimeout = options.TotalTimeout,
            Retry = options.Retry,
            LoggerFactory = options.LoggerFactory ?? loggerFactory,
        };
}
