using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace TypeSafeSharp;

// Bounded concurrency over the virtual SystemOneAsync, so a mock that overrides only that method
// drives a batch too (ADR-0007).
internal static class BatchRunner
{
    // Errors about the account rather than the item. Yielding them as items would turn a revoked
    // key into 10,000 failures (ADR-0006 rule 7).
    public static bool IsFatal(Exception exception)
        => exception is TypeSafeAuthenticationException
            or TypeSafePermissionDeniedException
            or TypeSafeNotFoundException
            or TypeSafeConfigurationException;

    // Not an iterator, so argument errors throw at the call, not at the first MoveNextAsync.
    public static IAsyncEnumerable<BatchItem<TItem>> Run<TItem>(
        TypeSafeClient client,
        IEnumerable<TItem> items,
        Func<TItem, SystemOneRequest> createRequest,
        BatchOptions? options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(items);
        Guard.NotNull(createRequest);
        var maxConcurrency = options?.MaxConcurrency ?? 4;
        if (maxConcurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), maxConcurrency, "BatchOptions.MaxConcurrency must be at least 1.");
        }

        return RunCore(client, items, createRequest, maxConcurrency, options?.RequestOptions, time, cancellationToken);
    }

    private static async IAsyncEnumerable<BatchItem<TItem>> RunCore<TItem>(
        TypeSafeClient client,
        IEnumerable<TItem> items,
        Func<TItem, SystemOneRequest> createRequest,
        int maxConcurrency,
        TypeSafeRequestOptions? requestOptions,
        TimeProvider time,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = new List<Task<BatchItem<TItem>>>(maxConcurrency);
        using var source = items.GetEnumerator();
        var index = 0;
        try
        {
            while (true)
            {
                while (running.Count < maxConcurrency && source.MoveNext())
                {
                    running.Add(RunOneAsync(client, index++, source.Current, createRequest, requestOptions, time, stop.Token));
                }

                if (running.Count == 0)
                {
                    yield break;
                }

                var finished = await Task.WhenAny(running).ConfigureAwait(false);
                running.Remove(finished);

                // RunOneAsync only throws for cancellation, disposal or a fatal error, all of which end the enumeration.
                yield return await finished.ConfigureAwait(false);
            }
        }
        finally
        {
            // Reached on completion, on cancellation, on a fatal error, and when the caller breaks out early.
            stop.Cancel();
            foreach (var task in running)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException || IsFatal(ex))
                {
                    // The in-flight work being stopped. The first fatal error is the one the caller sees.
                }
            }
        }
    }

    private static async Task<BatchItem<TItem>> RunOneAsync<TItem>(
        TypeSafeClient client,
        int index,
        TItem item,
        Func<TItem, SystemOneRequest> createRequest,
        TypeSafeRequestOptions? requestOptions,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var start = time.GetTimestamp();
        try
        {
            var response = await client.SystemOneAsync(createRequest(item), requestOptions, cancellationToken).ConfigureAwait(false);
            return new BatchItem<TItem>(index, item, response, null, time.GetElapsedTime(start));
        }
        catch (Exception ex) when ((ex is TypeSafeException or ArgumentException) && !IsFatal(ex))
        {
            return new BatchItem<TItem>(index, item, null, ex, time.GetElapsedTime(start));
        }
    }
}
