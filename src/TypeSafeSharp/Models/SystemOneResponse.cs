using System;
using System.Collections.Generic;

namespace TypeSafeSharp;

/// <summary>The result of a System One call.</summary>
public sealed class SystemOneResponse
{
    internal SystemOneResponse(string model, IReadOnlyDictionary<string, Answer> answers, Usage usage, string? requestId)
    {
        Model = model;
        Answers = answers;
        Usage = usage;
        RequestId = requestId;
    }

    /// <summary>The versioned model that answered, for example <c>jev-1.13.0</c> for <c>jev-latest</c>. Log it next to decisions.</summary>
    public string Model { get; }

    /// <summary>Question id to answer, exactly as the server sent them: nothing added, nothing dropped.</summary>
    public IReadOnlyDictionary<string, Answer> Answers { get; }

    /// <summary>Tokens the call used.</summary>
    public Usage Usage { get; }

    /// <summary>The <c>x-typesafe-request-id</c> response header, or null when the server sent none.</summary>
    public string? RequestId { get; }

    /// <summary>
    /// Returns the answer to a yes or no question. The server may leave a question unanswered;
    /// check <c>Answers.ContainsKey</c> first if that is possible for your questions.
    /// </summary>
    /// <param name="questionId">The id used in the request.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="questionId"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">The server did not answer <paramref name="questionId"/>. The message lists the ids it did answer.</exception>
    /// <exception cref="InvalidOperationException">The answer is of another kind. The message names it.</exception>
    public NoulAnswer GetNoul(string questionId) => Get<NoulAnswer>(questionId, "noul");

    /// <summary>
    /// Returns the answer to a question with named options. The server may leave a question
    /// unanswered; check <c>Answers.ContainsKey</c> first if that is possible for your questions.
    /// </summary>
    /// <param name="questionId">The id used in the request.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="questionId"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">The server did not answer <paramref name="questionId"/>. The message lists the ids it did answer.</exception>
    /// <exception cref="InvalidOperationException">The answer is of another kind. The message names it.</exception>
    public ChoiceAnswer GetChoice(string questionId) => Get<ChoiceAnswer>(questionId, "choice");

    /// <summary>
    /// Returns the answer to a question with scored levels. The server may leave a question
    /// unanswered; check <c>Answers.ContainsKey</c> first if that is possible for your questions.
    /// </summary>
    /// <param name="questionId">The id used in the request.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="questionId"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">The server did not answer <paramref name="questionId"/>. The message lists the ids it did answer.</exception>
    /// <exception cref="InvalidOperationException">The answer is of another kind. The message names it.</exception>
    public ScoreAnswer GetScore(string questionId) => Get<ScoreAnswer>(questionId, "score");

    private T Get<T>(string questionId, string kind)
        where T : Answer
    {
        Guard.NotNull(questionId);
        if (!Answers.TryGetValue(questionId, out var answer))
        {
            var answered = Answers.Count == 0 ? "none" : "'" + string.Join("', '", Answers.Keys) + "'";
            throw new KeyNotFoundException($"The server did not answer '{questionId}'. Answered: {answered}.");
        }

        return answer as T
            ?? throw new InvalidOperationException($"'{questionId}' was answered as {answer.Type}, not {kind}.");
    }
}
