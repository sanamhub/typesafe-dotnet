# TypeSafeSharp

[![NuGet](https://img.shields.io/nuget/v/TypeSafeSharp?logo=nuget)](https://www.nuget.org/packages/TypeSafeSharp)
[![DI](https://img.shields.io/nuget/v/TypeSafeSharp.Extensions.DependencyInjection?logo=nuget&label=DI)](https://www.nuget.org/packages/TypeSafeSharp.Extensions.DependencyInjection)
[![Downloads](https://img.shields.io/nuget/dt/TypeSafeSharp?logo=nuget)](https://www.nuget.org/packages/TypeSafeSharp)
[![CI](https://github.com/sanamhub/typesafe-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/sanamhub/typesafe-dotnet/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](https://github.com/sanamhub/typesafe-dotnet/blob/main/LICENSE)

Unofficial .NET client for [TypeSafe AI](https://docs.typesafe.ai)'s System One API and its Jev
model. For .NET teams who want the official JS and Python SDK behaviour (retries, errors, key
handling) on `netstandard2.0` and `net10.0`, with typed answers, dependency injection and
OpenTelemetry tracing.

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

Every public type is in the [API reference](https://github.com/sanamhub/typesafe-dotnet/wiki).

## Many requests at once

`EvaluateManyAsync` runs up to `MaxConcurrency` calls (default 4) and yields each result as it
finishes, with `Index` pointing back to the input. One item's failure comes back as that item; a
rejected key or a wrong model ends the loop, since every item would fail the same way.

```csharp
string[] tickets = ["My card was charged twice.", "The export button does nothing."];
var question = Question.Noul("The customer needs action today");

await foreach (var item in client.EvaluateManyAsync(
    tickets,
    ticket => new SystemOneRequest(ticket, new Dictionary<string, Question> { ["is_urgent"] = question }),
    new BatchOptions { MaxConcurrency = 4 }))
{
    Console.WriteLine(item.Succeeded
        ? $"{item.Index}: {item.Response!.GetNoul("is_urgent").Noul:P0}"
        : $"{item.Index}: failed, {item.Exception!.Message}");
}
```

C# 7.3 has no `await foreach`, so .NET Framework projects on the default language version walk
the enumerator by hand:

```csharp
var batch = client.EvaluateManyAsync(tickets, ticket => new SystemOneRequest(ticket, questions)).GetAsyncEnumerator();
try
{
    while (await batch.MoveNextAsync())
    {
        Console.WriteLine(batch.Current.Index + ": " + (batch.Current.Succeeded ? "ok" : batch.Current.Exception.Message));
    }
}
finally
{
    await batch.DisposeAsync();
}
```

## Install

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

## Configuration

Each setting resolves in order: the option value, then an environment variable, then a default.

| Setting | Option | Environment variable | Default |
| --- | --- | --- | --- |
| API key | `ApiKey` | `TYPESAFE_API_KEY` | none; missing throws |
| Base URL | `BaseUrl` | `TYPESAFE_BASE_URL` | `https://api.typesafe.ai` |
| Model | `DefaultModel` | `TYPESAFE_DEFAULT_MODEL` | `jev-latest` |

Reading everything from the environment is `new TypeSafeClient(new TypeSafeClientOptions())`.
The key is validated when the client is built: an empty key, whitespace inside it, a control
character, or a non-ASCII character each throws `TypeSafeConfigurationException`. An explicit
empty `ApiKey` does not fall back to the environment. Keep the key in the environment, in user
secrets or in a vault, never in source ([Security](#security)).

### Pointing at another endpoint

`BaseUrl` is an API root; the client appends `/v1/systemone`. Any gateway that speaks the same
route works:

| Gateway | Base URL | Model | Key |
| --- | --- | --- | --- |
| TypeSafe (default) | `https://api.typesafe.ai` | `jev-latest` | a TypeSafe key |
| [OpenJEV](https://openjev.sh/docs) | `https://api.openjev.sh` | `openjev` | a key from openjev.sh |
| `rev serve` (local) | `http://127.0.0.1:8421` | any open checkpoint | any placeholder |

OpenJEV is an independent community gateway to the same Jev model, with the same request and
response shape. The environment alone selects it:

```bash
export TYPESAFE_API_KEY=...          # your key from https://openjev.sh/dashboard
export TYPESAFE_BASE_URL=https://api.openjev.sh
export TYPESAFE_DEFAULT_MODEL=openjev
```

OpenJEV's own examples write the key as `OPENJEV_API_KEY`; that name is its convention, not this
SDK's, so copy the value into `TYPESAFE_API_KEY` or set `ApiKey` in code.

`rev serve` answers the same route from a local, open checkpoint with no key and no network. The
SDK still requires a key value, so set any placeholder and point the base URL at the local
server. Plain http is accepted only for loopback addresses.

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

Extra headers, a proxy or your own handlers: build the `HttpClient` yourself and pass it to
`new TypeSafeClient(httpClient, options)`, or add handlers to the builder `AddTypeSafe` returns.
The SDK never disposes or changes a client you pass, and its own headers win over your
`DefaultRequestHeaders` of the same name.

With .NET Aspire, keep the key a secret parameter in the AppHost and hand it over as the
environment variable the client reads:

```csharp
// fragment: AppHost project, needs Aspire.Hosting
var key = builder.AddParameter("typesafe-key", secret: true);
builder.AddProject<Projects.Api>("api").WithEnvironment("TYPESAFE_API_KEY", key);
```

### Your own resilience pipeline

Turn off the built-in retries and set both SDK timeouts longer than your pipeline's total, so
the SDK does not cut it off. Needs `Microsoft.Extensions.Http.Resilience`:

```csharp
// fragment: needs Microsoft.Extensions.Http.Resilience
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
| TypeSafeSharp | 0.1.0-alpha.1 |
| API (OpenAPI document) | 0.2.0 |
| `typesafe-sdk-js` | 0.6.0 |
| `typesafe-sdk-python` | 0.7.2 |

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
| [Wiki](https://github.com/sanamhub/typesafe-dotnet/wiki) | Public API reference and configuration |
| [docs/runbooks/release.md](docs/runbooks/release.md) | Release, post-release checks and rollback |
| [sample](sample) | Sample consumer: console, batch with NativeAOT, ASP.NET Core, .NET Framework at C# 7.3 |

## License

[MIT](LICENSE). Behaviour ported from the MIT-licensed official SDKs is credited in
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) (ADR-0002).

## Disclaimer

TypeSafeSharp is an independent, community project. It is not affiliated with, endorsed by or
supported by TypeSafe AI. "TypeSafe", "System One" and "Jev" are used only to say which service
the package talks to. For the official SDKs and support, see
[docs.typesafe.ai](https://docs.typesafe.ai).
