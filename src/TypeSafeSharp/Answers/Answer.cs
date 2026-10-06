namespace TypeSafeSharp;

/// <summary>
/// The server's answer to one question. Cast to <see cref="NoulAnswer"/>, <see cref="ChoiceAnswer"/>
/// or <see cref="ScoreAnswer"/>, or use the typed accessors on <see cref="SystemOneResponse"/>. A kind
/// this SDK does not know yet arrives as <see cref="UnknownAnswer"/>.
/// </summary>
public abstract class Answer
{
    private protected Answer() { }

    /// <summary>The wire type: <c>noul</c>, <c>choice</c>, <c>score</c>, or the type of an <see cref="UnknownAnswer"/>.</summary>
    public abstract string Type { get; }
}
