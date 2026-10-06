using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypeSafeSharp;

/// <summary>
/// One question for System One to answer about a request's state. Build one with
/// <see cref="Noul"/>, <see cref="Choice(JsonNode?, string[])"/>, <see cref="Score"/> or
/// <see cref="FromJson"/>.
/// </summary>
/// <remarks>
/// Questions are immutable and safe to share across concurrent calls: every factory method
/// deep clones the nodes it is given, so changing a node afterwards does not change the question.
/// </remarks>
public abstract class Question
{
    private protected Question(JsonNode? instructions) => Instructions = instructions;

    /// <summary>
    /// The wire type: <c>noul</c>, <c>choice</c>, <c>score</c>, or the <c>type</c> of an object
    /// passed to <see cref="FromJson"/>.
    /// </summary>
    public abstract string Type { get; }

    /// <summary>What the model should judge, as text or any JSON value. A clone of what was passed.</summary>
    public JsonNode? Instructions { get; }

    /// <summary>
    /// Creates a yes or no question. The answer is a probability between 0 and 1 that the
    /// instructions hold.
    /// </summary>
    /// <param name="instructions">What the model should judge.</param>
    /// <param name="whenTrue">Optional description of what a yes means. Sent as <c>criteria.true</c>.</param>
    /// <param name="whenFalse">Optional description of what a no means. Sent as <c>criteria.false</c>.</param>
    /// <returns>The question.</returns>
    public static NoulQuestion Noul(JsonNode? instructions, JsonNode? whenTrue = null, JsonNode? whenFalse = null)
        => new(instructions?.DeepClone(), whenTrue?.DeepClone(), whenFalse?.DeepClone());

    /// <summary>Creates a question whose answer is one of the named options, with no descriptions.</summary>
    /// <param name="instructions">What the model should judge.</param>
    /// <param name="options">The option names, at least one, each non-empty and distinct.</param>
    /// <returns>The question.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> is empty, or has a null, empty or duplicate name.
    /// </exception>
    public static ChoiceQuestion Choice(JsonNode? instructions, params string[] options)
    {
        Guard.NotNull(options);
        if (options.Length == 0)
        {
            throw new ArgumentException("A Choice question needs at least one option.", nameof(options));
        }

        var copy = new Dictionary<string, JsonNode?>(options.Length, StringComparer.Ordinal);
        foreach (var name in options)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Option names cannot be null or empty.", nameof(options));
            }

            if (copy.ContainsKey(name))
            {
                throw new ArgumentException($"Option '{name}' appears more than once.", nameof(options));
            }

            copy.Add(name, null);
        }

        return new ChoiceQuestion(instructions?.DeepClone(), new ReadOnlyDictionary<string, JsonNode?>(copy));
    }

    /// <summary>Creates a question whose answer is one of the named options, each with an optional description.</summary>
    /// <param name="instructions">What the model should judge.</param>
    /// <param name="options">Option name to description (null for none). At least one, names non-empty.</param>
    /// <returns>The question.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="options"/> is empty, or has a null or empty name.</exception>
    public static ChoiceQuestion Choice(JsonNode? instructions, IReadOnlyDictionary<string, JsonNode?> options)
    {
        Guard.NotNull(options);
        if (options.Count == 0)
        {
            throw new ArgumentException("A Choice question needs at least one option.", nameof(options));
        }

        var copy = new Dictionary<string, JsonNode?>(options.Count, StringComparer.Ordinal);
        foreach (var pair in options)
        {
            if (string.IsNullOrEmpty(pair.Key))
            {
                throw new ArgumentException("Option names cannot be null or empty.", nameof(options));
            }

            copy.Add(pair.Key, pair.Value?.DeepClone());
        }

        return new ChoiceQuestion(instructions?.DeepClone(), new ReadOnlyDictionary<string, JsonNode?>(copy));
    }

    /// <summary>
    /// Creates a question whose answer is a score on the given levels, lowest first. Each level is
    /// a description of what that score means.
    /// </summary>
    /// <param name="instructions">What the model should judge.</param>
    /// <param name="levels">At least two level descriptions, none null.</param>
    /// <returns>The question.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levels"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="levels"/> has fewer than two levels, or a null level.</exception>
    public static ScoreQuestion Score(JsonNode? instructions, params JsonNode[] levels)
    {
        Guard.NotNull(levels);

        // The JS SDK requires two levels; Python only one. One level is not a scale.
        if (levels.Length < 2)
        {
            throw new ArgumentException("A Score question needs at least two levels.", nameof(levels));
        }

        var copy = new JsonNode[levels.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            copy[i] = levels[i]?.DeepClone()
                ?? throw new ArgumentException($"Level {i} is null.", nameof(levels));
        }

        return new ScoreQuestion(instructions?.DeepClone(), new ReadOnlyCollection<JsonNode>(copy));
    }

    /// <summary>
    /// Wraps a raw question object, for question types this SDK does not model yet. The object is
    /// cloned and sent as is; only its <c>type</c> is checked.
    /// </summary>
    /// <param name="question">A question object with a string <c>type</c>, as in the API reference.</param>
    /// <returns>The question.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="question"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="question"/> has no <c>type</c>, or it is not a non-empty string.</exception>
    public static Question FromJson(JsonObject question)
    {
        Guard.NotNull(question);
        var clone = (JsonObject)question.DeepClone();
        if (clone["type"] is not JsonValue type
            || type.GetValueKind() != JsonValueKind.String
            || type.GetValue<string>() is not { Length: > 0 } name)
        {
            throw new ArgumentException("The question object needs a non-empty string 'type'.", nameof(question));
        }

        return new RawQuestion(clone, name);
    }
}
