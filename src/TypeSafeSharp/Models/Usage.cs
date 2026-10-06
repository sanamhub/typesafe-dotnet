namespace TypeSafeSharp;

/// <summary>Tokens the call used, as the server counted them.</summary>
public sealed class Usage
{
    internal Usage(long inputTokens, long outputTokens)
    {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }

    /// <summary>Tokens read from the state and questions.</summary>
    public long InputTokens { get; }

    /// <summary>Tokens the model produced.</summary>
    public long OutputTokens { get; }
}
