using System.Collections.Generic;
using System.Text.Json;

namespace TypeSafeSharp;

/// <summary>The answer to a question with scored levels.</summary>
public sealed class ScoreAnswer : Answer
{
    internal ScoreAnswer(double score, double confidence, IReadOnlyDictionary<int, JsonElement> legend, IReadOnlyDictionary<int, double> probabilities)
    {
        Score = score;
        Confidence = confidence;
        Legend = legend;
        Probabilities = probabilities;
        MostLikelyLevel = Mode(probabilities);
    }

    /// <inheritdoc/>
    public override string Type => "score";

    /// <summary>The expected level, a probability-weighted mean, so it can fall between levels (1.05, say).</summary>
    public double Score { get; }

    /// <summary>How certain the model is, from 0 to 1. What counts as enough is the caller's decision.</summary>
    public double Confidence { get; }

    /// <summary>Level number, from 0, to the description sent for that level.</summary>
    public IReadOnlyDictionary<int, JsonElement> Legend { get; }

    /// <summary>Level number to probability, as the server sent them.</summary>
    public IReadOnlyDictionary<int, double> Probabilities { get; }

    /// <summary>The level with the highest probability; the lower level on a tie. Use it when a whole level is needed rather than <see cref="Score"/>.</summary>
    public int MostLikelyLevel { get; }

    // Callers guarantee at least one entry: the reader rejects an empty map, the factory throws.
    private static int Mode(IReadOnlyDictionary<int, double> probabilities)
    {
        var best = int.MaxValue;
        var bestProbability = double.NegativeInfinity;
        foreach (var pair in probabilities)
        {
            if (pair.Value > bestProbability || (pair.Value == bestProbability && pair.Key < best))
            {
                best = pair.Key;
                bestProbability = pair.Value;
            }
        }

        return best;
    }
}
