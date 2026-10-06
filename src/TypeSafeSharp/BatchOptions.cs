namespace TypeSafeSharp;

/// <summary>Settings for <c>TypeSafeClient.EvaluateManyAsync</c>. Checked when the method is called.</summary>
public sealed class BatchOptions
{
    /// <summary>How many calls run at once. Default 4. Must be at least 1.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Per-call timeouts or retries for every item, or null for the client's settings.</summary>
    public TypeSafeRequestOptions? RequestOptions { get; set; }
}
