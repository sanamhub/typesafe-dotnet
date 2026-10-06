using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace TypeSafeSharp;

/// <summary>A question answered with one of its options. Created by <c>Question.Choice</c>.</summary>
public sealed class ChoiceQuestion : Question
{
    internal ChoiceQuestion(JsonNode? instructions, IReadOnlyDictionary<string, JsonNode?> options)
        : base(instructions) => Options = options;

    /// <inheritdoc/>
    public override string Type => "choice";

    /// <summary>Option name to description (null when none), in the order given. Sent as <c>criteria</c>.</summary>
    public IReadOnlyDictionary<string, JsonNode?> Options { get; }
}
