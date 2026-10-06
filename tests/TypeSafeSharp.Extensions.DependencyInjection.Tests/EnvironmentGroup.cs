namespace TypeSafeSharp.Extensions.DependencyInjection.Tests;

// The environment is process-wide, so every test that reads or writes it runs in this one
// collection, never in parallel with anything.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentGroup : ICollectionFixture<EnvironmentFixture>
{
    public const string Name = "Environment";
}

// Saves and clears the SDK's variables, then restores them. A maintainer's machine often has a
// real TYPESAFE_API_KEY set; clearing it keeps "no key" tests honest and the key out of output.
public sealed class EnvironmentFixture : IDisposable
{
    private static readonly string[] s_names = ["TYPESAFE_API_KEY", "TYPESAFE_BASE_URL", "TYPESAFE_DEFAULT_MODEL"];
    private readonly Dictionary<string, string?> _saved = [];

    public EnvironmentFixture()
    {
        foreach (var name in s_names)
        {
            _saved[name] = Environment.GetEnvironmentVariable(name);
        }

        Clear();
    }

    public static void Clear()
    {
        foreach (var name in s_names)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach (var pair in _saved)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
