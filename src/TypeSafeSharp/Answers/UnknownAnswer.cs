using System.Text.Json;

namespace TypeSafeSharp;

/// <summary>
/// An answer of a kind this SDK does not model yet, kept as the raw JSON so nothing the server
/// sent is lost. Upgrading the SDK may turn it into a typed answer.
/// </summary>
public sealed class UnknownAnswer : Answer
{
    internal UnknownAnswer(string type, JsonElement raw)
    {
        Type = type;
        Raw = raw;
    }

    /// <summary>The <c>type</c> the server sent.</summary>
    public override string Type { get; }

    /// <summary>The whole answer object as sent.</summary>
    public JsonElement Raw { get; }
}
