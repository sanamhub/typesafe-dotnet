using System;

namespace TypeSafeSharp;

// Ported from typesafe-sdk-python v0.7.1: strip outer whitespace, then reject what HttpClient
// would reject later with a confusing error. Messages name the rule and never echo the key.
internal static class ApiKey
{
    public const string EnvironmentVariable = "TYPESAFE_API_KEY";

    public static string Resolve(string? fromOptions)
    {
        if (fromOptions is not null)
        {
            // An explicit empty key is a mistake, not a request to read the environment.
            return Validate(fromOptions);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(fromEnvironment))
        {
            throw new TypeSafeConfigurationException(
                "No API key was provided. Set TypeSafeClientOptions.ApiKey or the TYPESAFE_API_KEY environment variable. Create a key at https://console.typesafe.ai/keys.");
        }

        return Validate(fromEnvironment!);
    }

    public static string Validate(string key)
    {
        var trimmed = key.Trim();
        if (trimmed.Length == 0)
        {
            throw new TypeSafeConfigurationException("The API key is empty.");
        }

        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                throw new TypeSafeConfigurationException("The API key contains whitespace inside it. Check for a pasted line break.");
            }

            if (char.IsControl(c))
            {
                throw new TypeSafeConfigurationException("The API key contains a control character.");
            }

            if (c > '~')
            {
                throw new TypeSafeConfigurationException("The API key contains a non-ASCII character. Check for a pasted quote or symbol.");
            }
        }

        return trimmed;
    }
}
