using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace TypeSafeSharp;

/// <summary>A System One call: the state to judge and the questions to answer about it.</summary>
public sealed class SystemOneRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="state">
    /// What to judge: text, or any JSON value. Not copied, so do not change it while a call that
    /// uses it is running.
    /// </param>
    /// <param name="questions">Question id to question, at least one. Copied, so later changes to the map do not affect the request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="questions"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="questions"/> is empty, or has a null or empty id or a null question.</exception>
    public SystemOneRequest(JsonNode state, IReadOnlyDictionary<string, Question> questions)
    {
        State = Guard.NotNull(state);
        Guard.NotNull(questions);
        if (questions.Count == 0)
        {
            throw new ArgumentException("A request needs at least one question.", nameof(questions));
        }

        var copy = new Dictionary<string, Question>(questions.Count, StringComparer.Ordinal);
        foreach (var pair in questions)
        {
            if (string.IsNullOrEmpty(pair.Key))
            {
                throw new ArgumentException("Question ids cannot be null or empty.", nameof(questions));
            }

            copy.Add(pair.Key, pair.Value ?? throw new ArgumentException($"Question '{pair.Key}' is null.", nameof(questions)));
        }

        Questions = new ReadOnlyDictionary<string, Question>(copy);
    }

    /// <summary>What to judge. Stored as given, not cloned.</summary>
    public JsonNode State { get; }

    /// <summary>Question id to question. The ids come back as the keys of the answers.</summary>
    public IReadOnlyDictionary<string, Question> Questions { get; }

    /// <summary>The model to ask, for example <c>jev-1.13.0</c>. Null uses the client's default model.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// Extra top-level request fields, for API features this SDK does not model yet. A key named
    /// <c>state</c>, <c>model</c> or <c>questions</c> is rejected when the request is sent.
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "A JsonObject is a value here, not a collection the type owns. C# 7.3 callers have no init accessor, and a get-only property would force them to mutate a shared default instead (PLAN.md section 3.2).")]
    public JsonObject? ExtraBody { get; set; }
}
