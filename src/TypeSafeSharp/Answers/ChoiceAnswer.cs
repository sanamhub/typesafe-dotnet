using System.Collections.Generic;

namespace TypeSafeSharp;

/// <summary>The answer to a question with named options.</summary>
public sealed class ChoiceAnswer : Answer
{
    internal ChoiceAnswer(string choice, double confidence, IReadOnlyDictionary<string, double> probabilities)
    {
        Choice = choice;
        Confidence = confidence;
        Probabilities = probabilities;
    }

    /// <inheritdoc/>
    public override string Type => "choice";

    /// <summary>The option the model picked.</summary>
    public string Choice { get; }

    /// <summary>How certain the model is, from 0 to 1. What counts as enough is the caller's decision.</summary>
    public double Confidence { get; }

    /// <summary>Option name to probability, as the server sent them.</summary>
    public IReadOnlyDictionary<string, double> Probabilities { get; }
}
