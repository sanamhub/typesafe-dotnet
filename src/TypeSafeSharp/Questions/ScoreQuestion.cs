using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace TypeSafeSharp;

/// <summary>A question answered with a score on its levels. Created by <see cref="Question.Score"/>.</summary>
public sealed class ScoreQuestion : Question
{
    internal ScoreQuestion(JsonNode? instructions, IReadOnlyList<JsonNode> levels)
        : base(instructions) => Levels = levels;

    /// <inheritdoc/>
    public override string Type => "score";

    /// <summary>The level descriptions, lowest first. The answer numbers them from 0. Sent as <c>criteria</c>.</summary>
    public IReadOnlyList<JsonNode> Levels { get; }
}
