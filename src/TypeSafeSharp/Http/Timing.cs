using System;
using System.Threading;
using System.Threading.Tasks;

namespace TypeSafeSharp;

// TimeProvider helpers that exist on both targets. On net10.0 Task.Delay has a TimeProvider
// overload, but netstandard2.0 does not, and ADR-0001 allows no #if for this.
internal static class Timing
{
    public static async Task DelayAsync(TimeProvider timeProvider, TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => done.TrySetCanceled(cancellationToken)))
        using (timeProvider.CreateTimer(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), done, delay, Timeout.InfiniteTimeSpan))
        {
            await done.Task.ConfigureAwait(false);
        }
    }

    // Cancels the source after the delay. Dispose the returned timer before the source.
    public static ITimer CancelAfter(TimeProvider timeProvider, CancellationTokenSource source, TimeSpan delay)
        => timeProvider.CreateTimer(
            static state =>
            {
                try
                {
                    ((CancellationTokenSource)state!).Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The attempt finished and disposed its source while the timer fired.
                }
            },
            source,
            delay,
            Timeout.InfiniteTimeSpan);
}
