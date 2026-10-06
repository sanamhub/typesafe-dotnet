# ADR-0005: Hand-rolled retries with the official SDK defaults

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06

## Context

The API returns `429 Too Many Requests` over either rate limit (250,000 tokens per second and
1,200 requests per minute for `jev-1.13.0`, "adjusting dynamically" per the Models page) and
`529 Overloaded` when the service is saturated. The API reference tells direct HTTP callers to
back off exponentially and says the official SDKs "handle this automatically".

Both official SDKs implement nearly the same policy. Read from `typesafe-sdk-js` `src/retry.ts`
at v0.6.0 and from the Python `RetryPolicy` reference:

| Setting | JS 0.6.0 | Python 0.7.1 |
| --- | --- | --- |
| Retries after the first attempt | 2 | 2 |
| First backoff, doubling | 500 ms | 0.5 s |
| Backoff cap | 5,000 ms | 5 s |
| Jitter (fraction subtracted) | 0.25 | 0.25 |
| Retried statuses | 408, 429, 500 to 599 | same |
| Server delay headers | `retry-after-ms`, then `Retry-After` (seconds or HTTP date) | same |
| Server delay cap, longer falls back to backoff | 60 s | none; the 30 s budget stops the retry instead |
| Retry connection errors, timeouts | yes, yes | yes, yes |
| Timeout | 10 s per attempt, including the body | 10 s per HTTP operation (connect, read, write, pool) |
| Total budget per call | none | 30 s |
| Retry marker header | `X-TypeSafe-Retry-Count: n` | `X-TypeSafe-Retry-Count: n` |

## Decision

1. **Implement the loop in the SDK, with no Polly or `Microsoft.Extensions.Http.Resilience`
   dependency.** It is about eighty lines and a table-driven test. A resilience library would be
   the largest dependency in the package, to do one thing.
2. **Match the table above, with Python's 30-second total budget, enforced more strictly.** A
   call never takes longer than `TotalTimeout` including delays. Each attempt's timeout is
   `min(AttemptTimeout, remaining budget)`, so the in-flight attempt is also cut at the budget;
   Python checks the budget only before sleeping. A retry whose delay would reach the budget is
   skipped and the last error is thrown. Both timeouts must be finite and greater than zero, else
   `ArgumentOutOfRangeException`; there is no infinite setting.

   JS has no budget; Python's is the safer default for server code that sits behind its own
   request timeout. Where the SDKs differ otherwise, we follow JS: a server delay over 60 s falls
   back to backoff, and the 10 s timeout covers the whole attempt, body included.
3. **Send `X-TypeSafe-Retry-Count` on retries** as both official SDKs do, so TypeSafe can tell
   retries from fresh traffic.
4. **`529` is retried** (it is in 500 to 599) and surfaces, when retries run out, as
   `TypeSafeOverloadedException`, a subclass of `TypeSafeInternalServerException`. Callers who
   care can catch it and shed load instead of paging someone.
5. **Two public knobs, and a per-call override.** `RetryPolicy` exposes only `MaxRetries`
   (default 2) and `RetryOnTimeout` (default `true`, the double-billing escape hatch). The rest
   of the table is internal constants: backoff 500 ms doubling to 5 s, jitter 0.25, statuses 408,
   429 and 500 to 599, `retry-after-ms` then `Retry-After`, server delay cap 60 s, connection
   errors retried. Any of them can become public later without a break.

   `TypeSafeRequestOptions.Retry` replaces the whole client policy for one call; it does not
   merge fields. To change one field, build a new policy, `new RetryPolicy { MaxRetries = 5 }`,
   which C# 7.3 callers can write too (no `with`). `RetryPolicy.None` disables retries, which is
   what a caller plugging in their own resilience handler sets (ADR-0009).
6. **Timeout versus cancellation.** Each attempt runs under a linked `CancellationTokenSource`
   that a timer cancels after the attempt timeout. If the caller's token fired, the SDK throws
   `OperationCanceledException` untouched (the .NET convention). If only the attempt timer fired,
   it throws `TypeSafeTimeoutException`.

   An `OperationCanceledException` caused by `HttpClient.Timeout` (caller token not cancelled,
   our timer not fired) also becomes `TypeSafeTimeoutException`. Caller cancellation and
   timeouts are never confused.
7. **Own `HttpClient` lifetime.** When the SDK creates the `HttpClient`, it sets
   `Timeout = Timeout.InfiniteTimeSpan` so only the SDK timers apply. On `net10.0` it uses
   `SocketsHttpHandler { PooledConnectionLifetime = 5 minutes }` so DNS changes are picked up
   without `IHttpClientFactory`. On `netstandard2.0` it uses
   `HttpClientHandler { MaxConnectionsPerServer = 64 }`, because .NET Framework defaults to 2
   connections per host, which would queue a batch of 4 and burn its attempt timeout. Both
   handlers set `AllowAutoRedirect = false`, so a redirect surfaces as `TypeSafeApiException`
   instead of a POST silently turning into a GET.

   The `netstandard2.0` asset running on .NET 8 or 9 gets no pooled connection lifetime; the
   README says so under Limits. The response body is buffered inside the attempt
   (`HttpCompletionOption.ResponseContentRead`, the default), so the attempt timeout covers body
   delivery as in JS. When the caller passes an `HttpClient`, the SDK never disposes it and never
   changes its settings, and the caller's `HttpClient.Timeout` also applies.
8. **Time comes from `TimeProvider`.** Delays and attempt timers use `TimeProvider.CreateTimer`,
   which works on `net10.0` and `net481` with no `#if` (in the box on `net10.0`,
   `Microsoft.Bcl.TimeProvider` on `netstandard2.0`). An internal constructor takes a
   `TimeProvider` and a `Func<double>` jitter source; tests reach it through `InternalsVisibleTo`
   and drive it with `FakeTimeProvider` (ADR-0012).
9. **Other exceptions from the handler chain** (for example Polly's `TimeoutRejectedException`
   or `BrokenCircuitException` from a caller's pipeline) become `TypeSafeConnectionException`
   with the original as the inner exception. Caller cancellation still wins.
10. **Dispose during a call.** The transport checks a disposed flag. If a client that owns its
    `HttpClient` is disposed while a call is running, the failed attempt throws
    `ObjectDisposedException(nameof(TypeSafeClient))` and is not retried.

## Consequences

Behaviour matches the official SDKs where they agree and follows JS where they differ, so
TypeSafe's docs on rate limits apply to .NET callers unchanged. Zero resilience dependencies.

Retrying a request that timed out can bill the same input twice, because the server may have
finished it. The API has no idempotency key today. Documented in the README under Limits; if
TypeSafe adds one, send it.

The loop is ours to maintain. The test matrix in PLAN.md section 5 covers every status, both
headers, the budget, and cancellation mid-delay.

## Alternatives considered

**Polly v8 or `Microsoft.Extensions.Http.Resilience` in the core package.** Rejected for the
dependency weight. Callers who already run a resilience pipeline can attach it to their own
`HttpClient` and set `RetryPolicy.None`. The DI package (ADR-0009) documents that recipe.

**No retries, leave it to the caller.** Rejected. The API docs promise SDK users automatic
backoff, and 429 is common while limits are being adjusted.

**JS defaults exactly (no total budget).** Rejected. Two retries at up to 60 seconds of
`Retry-After` each, plus three 10-second attempts, can hold a server thread for over two
minutes.
