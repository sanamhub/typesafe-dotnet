# ADR-0007: Async-only API, no streaming, a batch helper instead

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06

## Context

Four shape questions came up in review.

**Streaming.** Chat SDKs stream tokens with server-sent events, and the brief asked whether we
could offer streaming like other AI SDKs. The OpenAPI document
(`https://api.typesafe.ai/openapi.json`, version 0.2.0) has two operations, both returning a
single `application/json` body. Jev returns a probability per option, not generated text, so
there is nothing to emit incrementally. Neither official SDK streams.

What callers do need is many calls at once. The RAG cookbook sends one request per retrieved
passage, the top 12 per query. Doing that well in .NET means bounded concurrency, results as
they complete, and per-item failures that do not sink the batch. The line-search cookbook is the
other shape: it scores 218 line ids in one Choice request.

**Sync methods.** Python has `TypeSafeClient` and `AsyncTypeSafeClient`. The Azure SDK
guidelines ask for sync and async pairs. On `netstandard2.0` a sync HTTP call can only be sync
over async, which deadlocks under a `SynchronizationContext` (classic ASP.NET, WinForms, WPF).

**Mockability.** `TypeSafe.AI.Sdk` exposes `ITypeSafeClient`, but its `Models` property returns a
sealed type with an internal constructor, so `Models.ListAsync` cannot be faked. Adding a member
to a public interface is a breaking change on `netstandard2.0` (no default interface methods).

**Options mutation.** `TypeSafe.AI.Sdk` writes the API key back into the options object the
caller passed in (`TypeSafeClient.cs:444-448`), so a shared options instance ends up holding a
secret it did not have.

## Decision

1. **Async only.** Every I/O method returns `Task<T>` or `IAsyncEnumerable<T>`, ends in `Async`,
   and takes a `CancellationToken cancellationToken = default` as the last parameter. No sync
   methods in 1.x. Revisit only if a real sync consumer appears, and then only on `net10.0`
   using `HttpClient.Send`.
2. **No streaming API.** The README says so under Limits, with the reason above.
3. **`EvaluateManyAsync` batch helper**, returning `IAsyncEnumerable<BatchItem<TItem>>`. It has
   two overloads, `(items, createRequest, cancellationToken = default)` and
   `(items, createRequest, options, cancellationToken = default)` with `options` not optional,
   the same shape as `SystemOneAsync` (decision 4). PLAN.md section 3 has the full signatures.

   ```csharp
   await foreach (var item in client.EvaluateManyAsync(
       passages,                                       // IEnumerable<TItem>
       p => new SystemOneRequest(p.Text, questions),   // build one request per item
       new BatchOptions { MaxConcurrency = 8 },
       cancellationToken))
   {
       if (item.Exception is { } ex) { log.Failed(item.Index, ex); continue; }
       Use(item.Item, item.Response!);
   }
   ```

   Items are yielded in completion order. Each carries `Index` (its position in the input), the
   source item, the response or the exception, and `Elapsed`, which excludes time spent queued.
   `MaxConcurrency` defaults to 4. Retries and 429 handling come from the normal per-call policy.

   Account-wide failures end the enumeration by throwing: `TypeSafeAuthenticationException`
   (401), `TypeSafePermissionDeniedException` (403), `TypeSafeNotFoundException` (404) and
   `TypeSafeConfigurationException`. Any other `TypeSafeException`, and any `ArgumentException`,
   is yielded as a failed item. Caller cancellation cancels in-flight calls and throws
   `OperationCanceledException`.

   The public method is not an iterator, so it validates its arguments when called, not on the
   first `MoveNextAsync`. It hands off to a core iterator whose token parameter carries
   `[EnumeratorCancellation]`. Every item goes through the virtual `SystemOneAsync`, so a mock of
   that one method drives batch tests.

   The loop keeps up to `MaxConcurrency` calls running and yields each as `Task.WhenAny` returns
   it. There is no `SemaphoreSlim` and no `System.Threading.Channels` package on
   `netstandard2.0`. On that target `IAsyncEnumerable` needs `Microsoft.Bcl.AsyncInterfaces`,
   which also brings `IAsyncDisposable`.
4. **Virtual methods, no public interface.** Only
   `SystemOneAsync(SystemOneRequest, TypeSafeRequestOptions?, CancellationToken)` and
   `ModelsClient.ListAsync` are `virtual`, plus the `Models` property and
   `Dispose(bool)`. The other `SystemOneAsync` overloads,
   `(request, cancellationToken = default)` and `(state, questions, cancellationToken = default)`,
   and both `EvaluateManyAsync` overloads call the virtual one, so a mock sets up one method.
   `options` is not optional on the virtual overload because a probe showed that an optional
   `options` rejects `SystemOneAsync(request, ct)` with CS1503.

   `TypeSafeClient` and `ModelsClient` have protected parameterless constructors for mocking
   frameworks. Disposal is `public void Dispose()` plus `protected virtual void Dispose(bool
   disposing)` (CA1063). `TypeSafeModelFactory` builds response, answer and exception instances
   for tests. This is the Azure SDK pattern and it lets 1.x add members without a major version.
5. **Options are snapshotted.** The client copies `TypeSafeClientOptions` into an internal
   immutable settings object at construction and never writes to the caller's instance.
6. **Deferred to P5: typed Choice over an enum** (`Question.Choice<TEnum>`,
   `GetChoice<TEnum>`). It can be added later without a break. Until then the README shows
   `Enum.GetNames(typeof(T))` for the options and `Enum.Parse` for the answer.

## Consequences

The API has one call style and no deadlock traps. Batch work, which is the pattern the TypeSafe
cookbooks lean on, is one `await foreach`.

C# 7.3 callers (the .NET Framework default) cannot write `await foreach`. They call
`GetAsyncEnumerator()` and `MoveNextAsync()` by hand, and the README documents that loop. We do
not add a `Task<IReadOnlyList<...>>` overload for them.

Callers on older UI frameworks who want a blocking call write `.GetAwaiter().GetResult()`
themselves. Documented, not encouraged.

Mocking needs a framework that can override virtual members (Moq, NSubstitute, FakeItEasy) or a
hand-written subclass (README, "Testing your code"). Callers who want an interface for their own DI can wrap the client in
three lines.

## Alternatives considered

**Fake streaming** (an `IAsyncEnumerable` that yields each answer of one response). Rejected. The
whole response arrives at once, so this would add latency to nothing and mislead about what the
API does.

**`ITypeSafeClient` interface.** Rejected for the evolution cost above. A caller who wants one
can declare it.

**Channel-based producer and consumer batch API.** Rejected. `IAsyncEnumerable` is simpler and
composes with LINQ (`System.Linq.AsyncEnumerable` is in the box on .NET 10, and the
`System.Linq.AsyncEnumerable` package covers older targets).
