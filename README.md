# TypeSafeSharp

Unofficial .NET client for [TypeSafe AI](https://docs.typesafe.ai)'s System One API and its Jev
model. For .NET teams who want the official JS and Python SDK behaviour (retries, errors, key
handling) on `netstandard2.0` and `net10.0`, with typed answers, dependency injection and
OpenTelemetry tracing.

> **Status: planning.** Nothing is implemented or published yet. This repo holds the design:
> [docs/PLAN.md](docs/PLAN.md), the ADRs below (all proposed, awaiting approval) and
> [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md), the ordered task list for building it. The
> code in this README is the planned API. It compiles against a throwaway stub of PLAN section
> 3, not against a real package.

Not affiliated with or endorsed by TypeSafe AI. The official SDKs are
[typesafe-sdk-js](https://github.com/typesafe-ai/typesafe-sdk-js) and
[typesafe-sdk-python](https://github.com/typesafe-ai/typesafe-sdk-python).

```csharp
using TypeSafeSharp;

using var client = new TypeSafeClient(new TypeSafeClientOptions());  // reads TYPESAFE_API_KEY

var response = await client.SystemOneAsync(
    "I was charged twice this month. Please fix it today.",
    new Dictionary<string, Question>
    {
        ["category"] = Question.Choice("What is this message about?", "billing", "technical", "other"),
        ["is_urgent"] = Question.Noul("The customer needs action today"),
    });

var category = response.GetChoice("category");
Console.WriteLine($"{category.Choice} ({category.Confidence:P0}), urgent {response.GetNoul("is_urgent").Noul:P0}");
```

Create one `TypeSafeClient` per app and share it; it is thread-safe. Do not create one per
request.

Your own types go in as `JsonNode`:

- State from a POCO: `JsonSerializer.SerializeToNode(value, MyContext.Default.MyType)`.
- Choice options from an enum: `Question.Choice("Which team?", Enum.GetNames(typeof(Team)))`,
  then `(Team)Enum.Parse(typeof(Team), response.GetChoice("team").Choice)`.
- A question shape this package does not model yet: `Question.FromJson(jsonObject)`.

## Install (once published)

```bash
dotnet add package TypeSafeSharp
```

```bash
dotnet add package TypeSafeSharp.Extensions.DependencyInjection
```

| Target | Runs on |
| --- | --- |
| `net10.0` | .NET 10 and later. Trimming and NativeAOT clean. |
| `netstandard2.0` | .NET Framework 4.7.2 and later (tested on 4.8.1); 4.6.2 may work but is unsupported (its support ends 2027-01-12). .NET 8 and 9 until their end of support on 2026-11-10. |

Both assemblies are strong-named, so signed .NET Framework applications can reference them. The
API is callable from C# 7.3, the .NET Framework default: no `init` setters, no records.

## ASP.NET Core

```csharp
// appsettings.json: { "TypeSafe": { "DefaultModel": "jev-latest" } }. The key comes from
// user secrets, a vault or TYPESAFE_API_KEY, never from appsettings.json.
builder.Services.AddTypeSafe(builder.Configuration.GetSection("TypeSafe"));
```

`AddTypeSafe(o => ...)` configures in code instead. A missing key stops the app at startup, not
on the first request. For a second client (a pinned model, a gateway), register it yourself with
`AddKeyedSingleton`.

Tracing: `.WithTracing(t => t.AddSource("TypeSafeSharp"))` on your OpenTelemetry builder.

### Your own resilience pipeline

Turn off the built-in retries and set both SDK timeouts longer than your pipeline's total, so
the SDK does not cut it off. Needs `Microsoft.Extensions.Http.Resilience`:

```csharp
builder.Services
    .AddTypeSafe(o =>
    {
        o.Retry = RetryPolicy.None;
        o.AttemptTimeout = TimeSpan.FromSeconds(60);   // longer than the pipeline's total
        o.TotalTimeout = TimeSpan.FromSeconds(60);
    })
    .AddStandardResilienceHandler();
```

## What it adds over a hand-written `HttpClient` call

| Feature | Where |
| --- | --- |
| Retries, backoff and timeouts matching the official SDKs (2 retries, `Retry-After`, 529, 30 s budget) | [ADR-0005](docs/adr/0005-hand-rolled-retries-with-official-sdk-defaults.md) |
| One exception per status, with request id, never the key or your data in the message | [ADR-0006](docs/adr/0006-exception-hierarchy.md) |
| Typed answers, unknown answer types kept instead of thrown | [ADR-0004](docs/adr/0004-system-text-json-without-source-generation-and-jsonnode-values.md) |
| `EvaluateManyAsync`: many requests with bounded concurrency, results as they finish | [ADR-0007](docs/adr/0007-async-only-api-no-streaming-batch-helper.md) |
| `AddTypeSafe` over `IHttpClientFactory`, AOT-safe binding, validation on start | [ADR-0009](docs/adr/0009-dependency-injection-in-a-separate-package.md) |
| `ILogger`, and one `ActivitySource` span per call using the GenAI semantic conventions, no content recorded | [ADR-0010](docs/adr/0010-logging-tracing-and-metrics.md) |

## Testing your code

Subclass the client and override the one virtual `SystemOneAsync`. The other overloads and
`EvaluateManyAsync` all call it.

```csharp
class FakeTypeSafeClient : TypeSafeClient   // uses the protected constructor
{
    public override Task<SystemOneResponse> SystemOneAsync(
        SystemOneRequest request, TypeSafeRequestOptions? options, CancellationToken cancellationToken = default)
    {
        var answers = new Dictionary<string, Answer> { ["is_urgent"] = TypeSafeModelFactory.NoulAnswer(0.92) };
        return Task.FromResult(TypeSafeModelFactory.SystemOneResponse(
            "jev-1.13.0", answers, TypeSafeModelFactory.Usage(inputTokens: 120, outputTokens: 1)));
    }
}
```

Moq, NSubstitute and FakeItEasy can override it too. `TypeSafeModelFactory.ApiException(429)`
builds the exception for the failure path.

## Parity

| Checked against | Version |
| --- | --- |
| TypeSafeSharp | 0.1.0 (planned) |
| API (OpenAPI document) | 0.2.0 |
| `typesafe-sdk-js` | 0.6.0 |
| `typesafe-sdk-python` | 0.7.1 |

Every release updates this table (see the [release runbook](docs/runbooks/release.md)). The
feature-by-feature comparison is in
[PLAN section 2.1](docs/PLAN.md#21-official-sdk-behaviour-we-match).

## Limits

- **No streaming.** The API returns one JSON document per call and Jev generates no text, so
  there is nothing to stream. `EvaluateManyAsync` covers the "results as they arrive" case
  (ADR-0007).
- **Async only.** No sync methods (ADR-0007). C# 7.3 callers iterate `EvaluateManyAsync` with
  `GetAsyncEnumerator()` and `MoveNextAsync()`, since `await foreach` needs C# 8.
- **No `IChatClient`.** Jev answers questions about state. It is not a chat model (PLAN
  section 0.1).
- **No metrics yet.** 0.1 emits a span per call. Metrics wait for the GenAI conventions to
  settle (ADR-0010).
- **Retried timeouts can bill twice.** A timed-out request may have finished on the server, and
  the API has no idempotency key. Set `RetryOnTimeout = false` if that matters (ADR-0005).
- **`TYPESAFE_LOG_LEVEL` is not read.** Filter with `Logging:LogLevel:TypeSafeSharp` in the host
  (ADR-0010).
- **DNS changes on the `netstandard2.0` asset.** On .NET 8 and 9, which load that asset, the
  client cannot set a pooled connection lifetime. Pass your own `HttpClient` if the API's
  addresses change under a long-running process.
- **Unofficial.** Behaviour is ported from the official SDKs' source and changelogs. A nightly
  job diffs the live OpenAPI document and a weekly job runs the live tests against the real API
  (ADR-0012), but there can be a gap after a TypeSafe release.

## Versioning and support

- Only the latest minor version gets fixes.
- After 1.0, a TypeSafe wire change that forces a public API change is a major version.
- A target framework is dropped only in a minor (0.x) or major (1.x and later) release, with one
  release of notice.
- If TypeSafe ships an official .NET SDK, this package is deprecated on nuget.org, pointing to
  it.
- `CHANGELOG.md` entries that break something start with `**Breaking:**`.

## Security

Server-side only. Do not ship a key inside a WASM, MAUI or desktop app you distribute; the client
refuses to run in a browser (ADR-0008).

Keep the API key in an environment variable (`TYPESAFE_API_KEY`), user secrets, or a vault. Never
in source, `appsettings.json`, logs or issues. The client validates the key's format without
echoing it, never logs headers or bodies, and never copies request data into exception messages
(ADR-0006, ADR-0008).

## Documentation

| Document | Contents |
| --- | --- |
| [docs/PLAN.md](docs/PLAN.md) | API summary, existing .NET SDKs, public API, build, tests, CI, phases, risks, open questions |
| [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md) | Ordered implementation tasks with done criteria and compile-checked reference code |
| [docs/adr](docs/adr) | Architecture decisions 0001 to 0013 |
| [docs/runbooks/release.md](docs/runbooks/release.md) | Release, post-release checks and rollback |
| [sample](sample) | Sample consumer: console, batch with NativeAOT, ASP.NET Core, .NET Framework at C# 7.3 |

| ADR | Decision |
| --- | --- |
| [0001](docs/adr/0001-target-netstandard2.0-and-net10.0.md) | Target `netstandard2.0` and `net10.0` |
| [0002](docs/adr/0002-package-name-and-unofficial-positioning.md) | Package name `TypeSafeSharp`, marked unofficial |
| [0003](docs/adr/0003-flat-layout-instead-of-clean-architecture-projects.md) | One project with folders, not Clean Architecture projects |
| [0004](docs/adr/0004-system-text-json-without-source-generation-and-jsonnode-values.md) | System.Text.Json reader and writer, `JsonNode` values |
| [0005](docs/adr/0005-hand-rolled-retries-with-official-sdk-defaults.md) | Hand-rolled retries with official SDK defaults |
| [0006](docs/adr/0006-exception-hierarchy.md) | Exception hierarchy |
| [0007](docs/adr/0007-async-only-api-no-streaming-batch-helper.md) | Async-only, no streaming, batch helper |
| [0008](docs/adr/0008-api-key-handling-and-redaction.md) | API key handling, headers and bodies never logged |
| [0009](docs/adr/0009-dependency-injection-in-a-separate-package.md) | Dependency injection in a separate package |
| [0010](docs/adr/0010-logging-tracing-and-metrics.md) | Logging and tracing, metrics later |
| [0011](docs/adr/0011-versioning-and-release-with-trusted-publishing.md) | Versioning and release with trusted publishing |
| [0012](docs/adr/0012-test-strategy-and-api-drift-detection.md) | Test strategy and API drift detection |
| [0013](docs/adr/0013-strong-name-the-assemblies.md) | Strong-name the assemblies |

## License

MIT, once the repository is created (PLAN section 6). Behaviour ported from the MIT-licensed
official SDKs will be credited in `THIRD-PARTY-NOTICES.txt` (ADR-0002).

## Disclaimer

TypeSafeSharp is an independent, community project. It is not affiliated with, endorsed by or
supported by TypeSafe AI. "TypeSafe", "System One" and "Jev" are used only to say which service
the package talks to. For the official SDKs and support, see
[docs.typesafe.ai](https://docs.typesafe.ai).
