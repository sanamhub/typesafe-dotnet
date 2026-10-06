using System.Text.Json.Nodes;

namespace TypeSafeSharp;

/// <summary>A yes or no question. Created by <see cref="Question.Noul"/>.</summary>
public sealed class NoulQuestion : Question
{
    internal NoulQuestion(JsonNode? instructions, JsonNode? whenTrue, JsonNode? whenFalse)
        : base(instructions)
    {
        WhenTrue = whenTrue;
        WhenFalse = whenFalse;
    }

    /// <inheritdoc/>
    public override string Type => "noul";

    /// <summary>What a yes means, or null. Sent as <c>criteria.true</c>.</summary>
    public JsonNode? WhenTrue { get; }

    /// <summary>What a no means, or null. Sent as <c>criteria.false</c>.</summary>
    public JsonNode? WhenFalse { get; }
}
