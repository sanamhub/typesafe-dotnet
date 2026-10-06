namespace TypeSafeSharp;

/// <summary>The answer to a yes or no question.</summary>
public sealed class NoulAnswer : Answer
{
    internal NoulAnswer(double noul) => Noul = noul;

    /// <inheritdoc/>
    public override string Type => "noul";

    /// <summary>The probability, from 0 to 1, that the answer is yes.</summary>
    public double Noul { get; }
}
