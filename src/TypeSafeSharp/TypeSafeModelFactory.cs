using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace TypeSafeSharp;

/// <summary>
/// Builds SDK result and exception objects for callers' unit tests, since their constructors are
/// internal. The SDK itself never calls this class.
/// </summary>
public static class TypeSafeModelFactory
{
    private const string SystemOneEndpoint = "POST /v1/systemone";

    /// <summary>Creates a response, as the client returns from <c>SystemOneAsync</c>.</summary>
    /// <param name="model">The versioned model id, for example <c>jev-1.13.0</c>.</param>
    /// <param name="answers">Question id to answer. Copied.</param>
    /// <param name="usage">Token counts, from <see cref="Usage(long, long)"/>.</param>
    /// <param name="requestId">The request id, or null.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/>, <paramref name="answers"/> or <paramref name="usage"/> is null, or an answer is null.</exception>
    public static SystemOneResponse SystemOneResponse(string model, IReadOnlyDictionary<string, Answer> answers, Usage usage, string? requestId = null)
    {
        Guard.NotNull(model);
        Guard.NotNull(answers);
        Guard.NotNull(usage);
        var copy = new Dictionary<string, Answer>(answers.Count, StringComparer.Ordinal);
        foreach (var pair in answers)
        {
            copy.Add(pair.Key, pair.Value ?? throw new ArgumentNullException(nameof(answers), $"The answer for '{pair.Key}' is null."));
        }

        return new SystemOneResponse(model, new ReadOnlyDictionary<string, Answer>(copy), usage, requestId);
    }

    /// <summary>Creates token counts.</summary>
    /// <param name="inputTokens">Tokens read.</param>
    /// <param name="outputTokens">Tokens produced.</param>
    /// <returns>The usage.</returns>
    public static Usage Usage(long inputTokens, long outputTokens) => new(inputTokens, outputTokens);

    /// <summary>Creates the answer to a yes or no question.</summary>
    /// <param name="noul">The probability of yes, from 0 to 1.</param>
    /// <returns>The answer.</returns>
    public static NoulAnswer NoulAnswer(double noul) => new(noul);

    /// <summary>Creates the answer to a question with named options.</summary>
    /// <param name="choice">The option picked.</param>
    /// <param name="confidence">The confidence, from 0 to 1.</param>
    /// <param name="probabilities">Option name to probability. Copied.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="choice"/> or <paramref name="probabilities"/> is null.</exception>
    public static ChoiceAnswer ChoiceAnswer(string choice, double confidence, IReadOnlyDictionary<string, double> probabilities)
    {
        Guard.NotNull(choice);
        Guard.NotNull(probabilities);
        var copy = new Dictionary<string, double>(probabilities.Count, StringComparer.Ordinal);
        foreach (var pair in probabilities)
        {
            copy.Add(pair.Key, pair.Value);
        }

        return new ChoiceAnswer(choice, confidence, new ReadOnlyDictionary<string, double>(copy));
    }

    /// <summary>Creates the answer to a question with scored levels. <see cref="ScoreAnswer.MostLikelyLevel"/> is computed from <paramref name="probabilities"/>.</summary>
    /// <param name="score">The expected level.</param>
    /// <param name="confidence">The confidence, from 0 to 1.</param>
    /// <param name="legend">Level number, from 0, to its description. Copied, and each value cloned.</param>
    /// <param name="probabilities">Level number to probability, at least one. Copied.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="legend"/> or <paramref name="probabilities"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="probabilities"/> is empty.</exception>
    public static ScoreAnswer ScoreAnswer(double score, double confidence, IReadOnlyDictionary<int, JsonElement> legend, IReadOnlyDictionary<int, double> probabilities)
    {
        Guard.NotNull(legend);
        Guard.NotNull(probabilities);
        if (probabilities.Count == 0)
        {
            throw new ArgumentException("A score answer needs at least one level probability.", nameof(probabilities));
        }

        var legendCopy = new Dictionary<int, JsonElement>(legend.Count);
        foreach (var pair in legend)
        {
            legendCopy.Add(pair.Key, pair.Value.Clone());
        }

        var probabilityCopy = new Dictionary<int, double>(probabilities.Count);
        foreach (var pair in probabilities)
        {
            probabilityCopy.Add(pair.Key, pair.Value);
        }

        return new ScoreAnswer(score, confidence, new ReadOnlyDictionary<int, JsonElement>(legendCopy), new ReadOnlyDictionary<int, double>(probabilityCopy));
    }

    /// <summary>Creates an answer of a kind the SDK does not model.</summary>
    /// <param name="type">The wire type, for example <c>ranking</c>.</param>
    /// <param name="raw">The whole answer object. Cloned.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public static UnknownAnswer UnknownAnswer(string type, JsonElement raw) => new(Guard.NotNull(type), raw.Clone());

    /// <summary>Creates a model entry, as <c>Models.ListAsync</c> returns.</summary>
    /// <param name="name">The model id.</param>
    /// <param name="description">The description.</param>
    /// <param name="releaseDate">The release date, <c>YYYY-MM-DD</c>.</param>
    /// <returns>The model entry.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static ModelCard ModelCard(string name, string description, string releaseDate)
        => new(Guard.NotNull(name), Guard.NotNull(description), Guard.NotNull(releaseDate));

    /// <summary>
    /// Creates the exception the client throws for <paramref name="statusCode"/>: the
    /// <see cref="TypeSafeApiException"/> subclass for 400, 401, 403, 404, 422, 429, 529 and other
    /// 5xx statuses, or the base type for anything else. <see cref="TypeSafeApiException.Endpoint"/>
    /// is <c>POST /v1/systemone</c>.
    /// </summary>
    /// <param name="statusCode">The HTTP status, for example 429.</param>
    /// <param name="message">The message, or null for <c>"&lt;status&gt; status code (no message in body)"</c>.</param>
    /// <param name="requestId">The request id, or null.</param>
    /// <param name="retryAfter">Sets <see cref="TypeSafeRateLimitException.RetryAfter"/>; ignored for other statuses.</param>
    /// <returns>The exception, not thrown.</returns>
    public static TypeSafeApiException ApiException(int statusCode, string? message = null, string? requestId = null, TimeSpan? retryAfter = null)
    {
        var details = new ApiErrorDetails(
            message ?? statusCode.ToString(CultureInfo.InvariantCulture) + " status code (no message in body)",
            (HttpStatusCode)statusCode,
            SystemOneEndpoint,
            requestId,
            body: null,
            new Dictionary<string, IReadOnlyList<string>>(0, StringComparer.OrdinalIgnoreCase));
        return ErrorTypes.Create(details, retryAfter, Array.Empty<ValidationError>());
    }
}
