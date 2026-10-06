using System;

namespace TypeSafeSharp;

/// <summary>The outcome of one item in <c>TypeSafeClient.EvaluateManyAsync</c>: a response or the error for that item.</summary>
/// <typeparam name="TItem">The caller's item type.</typeparam>
public sealed class BatchItem<TItem>
{
    internal BatchItem(int index, TItem item, SystemOneResponse? response, Exception? exception, TimeSpan elapsed)
    {
        Index = index;
        Item = item;
        Response = response;
        Exception = exception;
        Elapsed = elapsed;
    }

    /// <summary>The zero-based position of the item in the input. Items arrive in completion order, so use this to match them up.</summary>
    public int Index { get; }

    /// <summary>The caller's item.</summary>
    public TItem Item { get; }

    /// <summary>The response, or null when the item failed.</summary>
    public SystemOneResponse? Response { get; }

    /// <summary>
    /// Why the item failed: a <see cref="TypeSafeException"/> that is about this item, or an
    /// <see cref="ArgumentException"/> from building its request. Null on success.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>How long the item took from building its request to its result, not counting time spent queued.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>True when <see cref="Response"/> is set.</summary>
    public bool Succeeded => Exception is null;
}
