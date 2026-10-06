# TypeSafeSharp engineering plan

Design and build plan for an unofficial .NET client for TypeSafe AI's System One API and its
Jev model. No implementation code ships with this document. Signatures, `.csproj` fragments and
YAML here are blueprints. The step-by-step build order for an implementer is
[IMPLEMENTATION.md](IMPLEMENTATION.md).

The sample consumer in [`sample/`](../sample) is written against section 3, and README code is
copied from it (AC-3.1). It references the packages from nuget.org, not the projects, so it
checks what users install; its own `Directory.Build.props`, `Directory.Packages.props`,
`NuGet.Config` and `.editorconfig` keep the repository's library settings out. The helper code in IMPLEMENTATION.md was built on both
targets with the analyzer settings of section 4.3 and run on `net10.0` and `net481`.

| Field | Value |
| --- | --- |
| Status | Proposed. Nothing implemented. ADR-0001 to ADR-0013 await approval. |
| API | `https://api.typesafe.ai`, OpenAPI 0.2.0, model `jev-latest` = `jev-1.13.0` |
| Behaviour reference | `typesafe-sdk-js` 0.6.0 (2026-09-15), `typesafe-sdk-python` 0.7.2 (2026-09-26, adds only an `http2` extra over 0.7.1), both MIT |
| Packages | `TypeSafeSharp`, `TypeSafeSharp.Extensions.DependencyInjection` (ADR-0002, ADR-0009) |
| Target frameworks | `netstandard2.0;net10.0`, AOT clean on `net10.0` (ADR-0001) |
| Support matrix | `netstandard2.0` supports .NET Framework 4.7.2 and later (tested on 4.8.1); 4.6.2 may work but is unsupported (its support ends 2027-01-12); .NET 8 and 9 until their end of support on 2026-11-10 |
| Distribution | Public on nuget.org, MIT, strong-named (ADR-0013), trusted publishing (ADR-0011) |
| Conventions | From `sanamhub/ada-csharp`: layout, `Directory.Build.props`, CI, release, ADR format, writing style |
| Standards | ADRs in `/docs/adr`, immutable once accepted. OWASP ASVS for secret handling. 80% line coverage target. SemVer with a changelog. Green CI to merge; the single-maintainer review exception is in `CLAUDE.md` (approved by Sanam). |

---

## 0. Design stance

This is a client for one POST and one GET. The service does the hard part. Every type we add
is something a user has to learn, so the plan is judged by how little it adds on top of parity
with the official SDKs, and by whether each addition is something .NET users need that the
official SDKs cannot give them.

Five facts drive the rest.

**The wire format is small and loose.** State, instructions and every criteria description
accept string, object, or array, and answers are a discriminated union on `type` (section 1).

**The API breaks.** In its first two weeks Score criteria changed from an integer-keyed map to an
ordered array (JS and Python 0.6.0, 2026-09-15). The SDK has to tolerate new answer kinds and
catch server changes in CI (ADR-0012).

**Rate limits are real and moving.** 1,200 requests per minute and 250,000 tokens per second,
"adjusting dynamically". Retry behaviour is part of the contract, not an extra (ADR-0005).

**The state is customer data.** Support tickets, contracts, chat logs. Nothing the SDK writes
(logs, spans, exception messages) may contain it or the API key (ADR-0008).

**The .NET market is already crowded.** Seventeen community packages in two weeks (section 2.3).
An eighteenth is only worth shipping if it is the one a careful team would pick: reach
(`netstandard2.0`, strong-named, callable from C# 7.3), correctness, and a clean AOT and
telemetry story, in one package with few dependencies.

### 0.1 What this plan does not do

| Cut | Reason |
| --- | --- |
| Streaming | The API has no streaming endpoint and Jev generates no text. Faking it would add latency to nothing. ADR-0007. |
| Sync methods | Sync over async on `netstandard2.0` deadlocks under a `SynchronizationContext`. ADR-0007. |
| Clean Architecture project split | Four assemblies for one HTTP call. ADR-0003. |
| Polly, `Microsoft.Extensions.Http.Resilience` or System.ClientModel in the core | The largest dependencies in the graph for an eighty-line loop. Callers can add their own (section 3.7). ADR-0005. |
| `IChatClient` implementation | Jev is a decision model, not a chat model. A guardrail adapter and an `IEvaluator` adapter are P5 candidates (section 7). |
| Code generation from OpenAPI | The document omits retries, headers and validation, which is most of the work. Used as a contract test instead. ADR-0012. |
| Provider failover | JevSharp's failover breaks when model IDs differ per provider (section 2.3). Callers who want failover register a second client with `AddKeyedSingleton`. |
| Response mapping to user classes (Python `response_model`) | P5. Typed accessors cover the common case. |
| Enum-typed Choice, per-call headers, keyed DI, metrics | P5. Today `Enum.GetNames` plus `Enum.Parse`, an `HttpClient` with `DefaultRequestHeaders`, and `AddKeyedSingleton` cover them. Metrics wait for the GenAI conventions to settle (ADR-0010). |
| Client-side limits (255 options, 10 levels, 64k tokens) | Enforced by the server, and they will change. Section 3.2 lists what we do validate. |
| `TYPESAFE_LOG_LEVEL` | The .NET host owns log filtering. ADR-0010. |
| Aspire integration, `IHealthCheck` | Nothing for Aspire to host, and a health check would spend rate limit and fail readiness during a TypeSafe outage. README recipe only. |

---

## 1. The API we wrap

Sources: `https://docs.typesafe.ai/api.md`, `https://api.typesafe.ai/openapi.json` (0.2.0), and
the Models page, all read 2026-09-24.

### 1.1 Operations

| Operation | Request | Response |
| --- | --- | --- |
| `POST /v1/systemone` | `{ state, model, questions: { <id>: Question } }` | `{ model, answers: { <id>: Answer }, usage: { input_tokens, output_tokens } }` |
| `GET /v1/models` | none | `{ models: [ { name, description, release_date } ] }` |

Auth is `Authorization: Bearer <key>`. Keys come from `https://console.typesafe.ai/keys`.

### 1.2 Questions

| Type | `criteria` | Answer fields |
| --- | --- | --- |
| `noul` | optional `{ "true": open or null, "false": open or null }` | `noul` (0 to 1, probability of yes) |
| `choice` | required map, option name to open value or null, max 255 | `choice`, `confidence`, `probabilities` (by option) |
| `score` | required ordered array of open values (no nulls), 2 to 10 (schema says `minItems: 1`, docs say at least 2) | `score` (probability weighted, may fall between levels), `confidence`, `legend` and `probabilities` keyed by level index as a string |

"Open value" means string, JSON object, or JSON array. `instructions` is an open value or null,
optional in the schema for all three types and marked required by the docs. `state` is an open
value and never null. Question ids are chosen by the caller and never reach the model.

### 1.3 Errors and headers

| Status | Meaning | Retried by official SDKs |
| --- | --- | --- |
| 401 | Missing or bad key | no |
| 422 | Validation failed, FastAPI `detail: [{ loc, msg, type, input, ctx }]` | no |
| 429 | Rate limited; the docs mention `Retry-After`, and both SDKs also parse `retry-after-ms` | yes |
| 529 | Overloaded | yes (500 to 599) |

The 422 `input` and `ctx` fields can echo the request, which is customer data. The SDK never
reads them (ADR-0006).

Response header `x-typesafe-request-id` identifies a request for support. Both official SDKs send
`User-Agent: typesafe-sdk/<version>`, `X-TypeSafe-SDK`, `X-TypeSafe-Runtime`, and
`X-TypeSafe-Retry-Count: <n>` on retries. We send the same headers with our own identity
(section 3.1).

### 1.4 Configuration contract

| Env var | Meaning | Default | Ours |
| --- | --- | --- | --- |
| `TYPESAFE_API_KEY` | API key | none, required | read |
| `TYPESAFE_BASE_URL` | API root | `https://api.typesafe.ai` | read |
| `TYPESAFE_DEFAULT_MODEL` | default model | `jev-latest` | read |
| `TYPESAFE_LOG_LEVEL` | SDK log level | `warn` (JS), unset (Python) | **not read**, ADR-0010 |

Explicit options win over environment variables, which win over defaults. Empty or whitespace
environment values are ignored (JS rule). An explicit empty API key is an error, not a fallback
(Python 0.7.1 rule).

### 1.5 Models and pricing

`jev-latest` and `jev-preview` both point to `jev-1.13.0` today. Price is $0.042 per million
input tokens, output free. Context is 64k tokens per request, 32k for state plus the longest
question. The response's `model` field reports the versioned ID, which callers should log when
they tune confidence thresholds.

### 1.6 Gateways

Python's docs show the same client against two gateways by changing `base_url` and `model`:

| Gateway | Base URL | Model |
| --- | --- | --- |
| OpenRouter | `https://openrouter.ai/api` | `~typesafe/jev-latest` |
| Vercel AI Gateway | `https://ai-gateway.vercel.sh/typesafe` | `typesafe-ai/jev` |

We support gateways only through `BaseUrl` and `DefaultModel`, and document the two rows above as
"documented by TypeSafe, not tested by us" until someone has a gateway key (section 9, Q5).

---

## 2. Existing SDKs and where we add value

### 2.1 Official SDK behaviour we match

| Behaviour | JS 0.6.0 | Python 0.7.2 | TypeSafeSharp |
| --- | --- | --- | --- |
| Question builders | `noul()`, `choice()`, `score()`, raw objects passed through | `Noul`, `Choice`, `Score`, raw dicts | `Question.Noul/Choice/Score`, `Question.FromJson` |
| Validation before send | non-empty questions, Score criteria a list of 2 or more | non-empty questions, Score criteria non-empty, dict questions need `type` and `criteria` | JS rules plus section 3.2, as `ArgumentException` |
| Typed results | inferred from `const` criteria | `nouls`, `choices`, `scores` maps, `response_model` | `GetNoul/GetChoice/GetScore` |
| Unknown answer kind | passed through | logged and skipped | `UnknownAnswer` plus one warning |
| Extra request fields | extra properties forwarded | `extra_body` | `ExtraBody` |
| Retries | ADR-0005 table | ADR-0005 table | JS policy with Python's 30 s budget, enforced more strictly |
| Key validation | presence | strip, reject whitespace, control, non-ASCII | Python's rules (ADR-0008) |
| Request id | `requestId`, undefined when absent | `request_id`, raises when absent | `RequestId`, null when absent |
| Raw response | `withResponse()`, `asResponse()` | `raw_http_response` | `UnknownAnswer.Raw`, `TypeSafeApiException.Body`; full raw access P5 |
| Logging | console, redacted headers, bodies at debug | `typesafe_sdk` logger, same | `ILogger`; headers and bodies never logged (ADR-0008) |
| Browser use | refused (`refuseBrowser`) | n/a | refused (section 3.1) |
| Sync client | n/a | `TypeSafeClient` | none (ADR-0007) |
| HTTP/2 | no (`fetch`) | opt-in `http2` extra (0.7.2) | on `net10.0`, falling back to 1.1; 1.1 on `netstandard2.0` |

### 2.2 How modern .NET SDKs are built

| Practice | OpenAI 2.14.0 | Anthropic 12.50.0 | Azure guidelines | Ours |
| --- | --- | --- | --- | --- |
| TFMs | ns2.0, net8, net10 | ns2.0, net8, net9 | n/a | ns2.0, net10 |
| Retries | System.ClientModel pipeline policy | hand-rolled, 2 retries, `Retry-After` uncapped | pipeline | hand-rolled, 2 retries, capped |
| Unions | generated | custom converter with `JsonElement` fallback | n/a | hand-written reader with `UnknownAnswer` |
| Mocking | virtual methods, model factory | interface plus virtual | virtual, protected ctor, `ModelFactory` | virtual, protected ctor, `TypeSafeModelFactory` |
| Exceptions | one `ClientResultException` | one per status | one `RequestFailedException` | one per status |
| Async naming | `Async` suffix, sync pairs | no suffix, async only | `Async` suffix, sync pairs | `Async` suffix, async only |
| DI | in core (`AddChatClient`) | none | n/a | separate package |
| Telemetry | experimental `ActivitySource` and `Meter`, GenAI semconv | none found | `ActivitySource` | `ActivitySource`, GenAI semconv; metrics P5 |
| Publishing | trusted publishing | long-lived API key | n/a | trusted publishing |

OpenAI and Azure use one exception type because their generated pipelines share it across many
services. That reason does not apply to two endpoints, and the official TypeSafe SDKs and the
Anthropic C# SDK use one type per status.

`Microsoft.Extensions.AI` has no chat abstraction that fits a decision API, but
`Microsoft.Extensions.AI.Evaluation`'s `IEvaluator` does: a Noul or Score over a model's output
is an evaluation metric. That is a P5 package (section 7), not part of the core.

### 2.3 Community .NET packages

Reviewed 2026-09-24. "Yes" means verified in source or package metadata. Download counts and
owners are in ADR-0002.

| Feature | `TypeSafe.AI.Sdk` (hardkoded) | `JevSharp` | `Jev.Net` | `TypeSafeAI` (Hawxy) | `tryAGI.TypeSafeAI` | TypeSafeSharp (planned) |
| --- | --- | --- | --- | --- | --- | --- |
| TFMs | ns2.0, net10 | net10 | net8, net10 | net8, net10 | net10 | ns2.0, net10 |
| .NET Framework | yes, untested | no | no | no | no | yes, tested on net481 at C# 7.3 |
| Strong-named | no | no | no | no | no | yes |
| AOT or trim safe | no (reflection STJ) | no flag, reflection path | yes, CI smoke | claimed | claimed | yes, CI smoke with warnings as errors |
| Heavy deps in core | `Microsoft.Extensions.Http` | Http.Resilience, Polly | none | Polly.Core, DI, Options, Http | MEAI, MEAI.Evaluation, Http | none (Logging.Abstractions) |
| Unknown answer kind | whole call fails | whole call fails | kept | unknown | unknown | kept, with kind name |
| Early key validation (Python 0.7.1) | no | partial | no | unknown | unknown | yes |
| Retries match official | yes, no budget | no (fixed 5 s, `Retry-After` off) | yes, budget leaks | Polly | unknown | yes, budget enforced across attempts |
| `x-typesafe-request-id` | yes | no | yes, throws if absent (as Python does) | unknown | unknown | yes, nullable |
| Enum-typed Choice | no | no | no | claimed | unknown | planned for P5 |
| Batch helper | no | limiter only | no | unknown | unknown | `EvaluateManyAsync` |
| OpenTelemetry | no | no | yes | unknown | unknown | traces; metrics in P5 |
| DI with `IConfiguration` | no binding | no binding | none | yes | yes | yes, AOT-safe binding, `ValidateOnStart` |
| `/v1/models` | yes | no | yes | unknown | unknown | yes |
| Known defects | mutates caller options, raw `InvalidOperationException` on bad answers | failover breaks, no `InnerException` | logs bodies unredacted (as both official SDKs do) | unknown | unknown | n/a |

### 2.4 Build, or contribute?

Contributing to one of the seventeen would reach users sooner. Against it: the one package with
`netstandard2.0` (`TypeSafe.AI.Sdk`) would need its JSON layer replaced to become AOT safe, a
rewrite of its core rather than a PR. The packages with the cleanest internals (`Jev.Net`,
Hawxy's) chose `net8.0` and above, and moving to `netstandard2.0` is a direction change their
owners did not ask for. Several packages are two to five days old with one author.

Recommendation: build, keep the scope in section 3, and revisit at P3. If an existing package
has closed the gaps in the right-hand column by then, offer our tests and fixes upstream instead
of publishing (section 9, Q2).

### 2.5 Where TypeSafeSharp adds value

In priority order. Each maps to an acceptance criterion.

1. **Reach without compromise.** One strong-named package for .NET Framework 4.7.2 through .NET 10
   NativeAOT, callable from C# 7.3, tested on both runtimes (AC-4.3, AC-4.4).
2. **Parity with the official SDKs**, including the 0.7.1 key validation and the 30 s budget, with
   each rule cited to its source (AC-3.4, AC-5.2).
3. **Survives API change.** Unknown answer kinds degrade to `UnknownAnswer`, unknown question
   kinds go through `Question.FromJson`, and server schema drift fails a nightly job, not a user
   (AC-3.6, AC-3.13, AC-5.6).
4. **Observability by default.** GenAI semconv spans, no content recorded (AC-3.9).
5. **.NET idioms the official SDKs cannot express.** `IAsyncEnumerable` batch, AOT-safe
   `IConfiguration` binding with startup validation (AC-3.7, AC-3.8).
6. **Small dependency graph.** The `net10.0` asset references
   `Microsoft.Extensions.Logging.Abstractions` only (AC-4.2).

---

## 3. Public API

Root namespace `TypeSafeSharp`. Everything below is public unless marked internal. Nullable
annotations are part of the contract.

Two rules shape every type here:

- **No `init` setters, no public records.** .NET Framework projects default to C# 7.3, which
  cannot call an `init` setter or use `with`. Configurable types use `{ get; set; }`; response
  types have get-only properties.
- **One virtual method per operation.** Convenience overloads call it, so a mock sets up one
  method.

### 3.1 Client

```csharp
public class TypeSafeClient : IDisposable
{
    protected TypeSafeClient();                                                    // mocking only
    public TypeSafeClient(string apiKey);
    public TypeSafeClient(TypeSafeClientOptions options);                          // null ApiKey: TYPESAFE_API_KEY
    public TypeSafeClient(HttpClient httpClient, TypeSafeClientOptions options);   // both required
    public virtual ModelsClient Models { get; }
    public Task<SystemOneResponse> SystemOneAsync(SystemOneRequest request, CancellationToken cancellationToken = default);
    public virtual Task<SystemOneResponse> SystemOneAsync(SystemOneRequest request, TypeSafeRequestOptions? options, CancellationToken cancellationToken = default);
    public Task<SystemOneResponse> SystemOneAsync(JsonNode state, IReadOnlyDictionary<string, Question> questions, CancellationToken cancellationToken = default);
    public IAsyncEnumerable<BatchItem<TItem>> EvaluateManyAsync<TItem>(IEnumerable<TItem> items, Func<TItem, SystemOneRequest> createRequest, CancellationToken cancellationToken = default);
    public IAsyncEnumerable<BatchItem<TItem>> EvaluateManyAsync<TItem>(IEnumerable<TItem> items, Func<TItem, SystemOneRequest> createRequest, BatchOptions? options, CancellationToken cancellationToken = default);
    public void Dispose();                                                         // disposes only an HttpClient it created
    protected virtual void Dispose(bool disposing);
}

public class ModelsClient { protected ModelsClient(); public virtual Task<IReadOnlyList<ModelCard>> ListAsync(CancellationToken cancellationToken = default); }
public sealed class ModelCard { public string Name { get; } public string Description { get; } public string ReleaseDate { get; } }  // YYYY-MM-DD as sent; DateOnly is not on ns2.0
```

The client is thread-safe: create one per app and share it, not one per request. Internally a
second constructor takes a `TimeProvider`, a jitter source and a browser check for tests
(reached through `InternalsVisibleTo`).

There is no public parameterless constructor, unlike `new TypeSafeClient()` in JS and Python.
The protected one exists for mocking frameworks (ADR-0007), and C# cannot have both. Reading
everything from the environment is `new TypeSafeClient(new TypeSafeClientOptions())`.

Two overload shapes were checked with the compiler. `options` is required on the `HttpClient`
constructor because an SDK-style `net4x` project does not reference `System.Net.Http`, and an
optional second parameter makes every one-argument constructor call also match that overload
and fail with CS0012. On `SystemOneAsync`, `TypeSafeRequestOptions? options = null` as the
second parameter makes `SystemOneAsync(request, cancellationToken)` fail with CS1503, so
`options` is required in its own overload.

Headers on every request: `Authorization: Bearer <key>`, `Accept: application/json`,
`Content-Type: application/json` on POST, `User-Agent: TypeSafeSharp/<version>`,
`X-TypeSafe-SDK: TypeSafeSharp/<version>`, `X-TypeSafe-Runtime`, and
`X-TypeSafe-Retry-Count: <n>` on retries. Never `typesafe-sdk/...`, which would make an
unofficial client look like official traffic (ADR-0002). Callers who need extra headers pass
their own `HttpClient` with `DefaultRequestHeaders`.

`X-TypeSafe-Runtime` is `RuntimeInformation.FrameworkDescription + " (" + os + "; " +
RuntimeInformation.OSArchitecture + ")"`, where `os` is `windows`, `linux`, `osx` or `other`
from `RuntimeInformation.IsOSPlatform`, and non-ASCII characters become `_`. It is not
`Environment.Version`, which is `4.0.30319.42000` on every .NET Framework.

Every request sets `Headers.ExpectContinue = false`, which saves a round trip on .NET Framework.
Both handler branches of `HttpDefaults.CreateHandler` set `AllowAutoRedirect = false`, so a
redirect surfaces as `TypeSafeApiException` instead of a POST silently turning into a GET.

On `net10.0` every request asks for HTTP/2 with `RequestVersionOrLower`, so a batch shares one
TLS connection and a proxy that only speaks 1.1 still works. The API serves HTTP/2 (checked
2026-10-06), and Python 0.7.2 offers it as an extra. Callers cannot opt in through their own
`HttpClient`, since `DefaultRequestVersion` does not apply to `SendAsync(HttpRequestMessage)`.
`netstandard2.0` stays on 1.1, because .NET Framework's handler rejects 2.0.

**Browser guard.** The core assembly carries `[assembly: UnsupportedOSPlatform("browser")]`, so
Blazor WASM consumers get a CA1416 warning. The internal constructor throws
`TypeSafeConfigurationException("TypeSafeClient runs server-side only; a browser app would expose the API key.")`
when `RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER"))` is true, as the JS SDK's
`refuseBrowser` does.

**Dispose during a call.** The transport checks a disposed flag. If the owned client was
disposed, a failed attempt throws `ObjectDisposedException(nameof(TypeSafeClient))` and is not
retried.

### 3.2 Requests and questions

```csharp
public sealed class SystemOneRequest
{
    public SystemOneRequest(JsonNode state, IReadOnlyDictionary<string, Question> questions);
    public JsonNode State { get; }                                   // not cloned; do not mutate during a call
    public IReadOnlyDictionary<string, Question> Questions { get; }  // copied at construction
    public string? Model { get; set; }                               // null: client DefaultModel
    public JsonObject? ExtraBody { get; set; }                       // forward-compatible fields
}

public abstract class Question
{
    private protected Question(JsonNode? instructions);
    public abstract string Type { get; }                             // "noul", "choice", "score", or the FromJson type
    public JsonNode? Instructions { get; }
    public static NoulQuestion Noul(JsonNode? instructions, JsonNode? whenTrue = null, JsonNode? whenFalse = null);
    public static ChoiceQuestion Choice(JsonNode? instructions, params string[] options);
    public static ChoiceQuestion Choice(JsonNode? instructions, IReadOnlyDictionary<string, JsonNode?> options);
    public static ScoreQuestion Score(JsonNode? instructions, params JsonNode[] levels);
    public static Question FromJson(JsonObject question);            // raw escape hatch
}

public sealed class NoulQuestion : Question   { public JsonNode? WhenTrue { get; } public JsonNode? WhenFalse { get; } }
public sealed class ChoiceQuestion : Question { public IReadOnlyDictionary<string, JsonNode?> Options { get; } }
public sealed class ScoreQuestion : Question  { public IReadOnlyList<JsonNode> Levels { get; } }
```

Naming notes. `WhenTrue` and `WhenFalse` rather than `True` and `False`, which read badly as
C# members. `Options` and `Levels` rather than `Criteria`, because the wire name covers three
different shapes and the .NET type can say which one it is. The JSON names stay `criteria`.

Questions are immutable and safe to share across concurrent calls. The factory methods deep
clone every `JsonNode` they receive, and serialization writes nodes with `WriteTo` so a caller's
node is never given a new parent (ADR-0004). To build state from a typed object, call
`JsonSerializer.SerializeToNode(value, MyContext.Default.MyType)` (README).

`Question.FromJson` is the escape hatch for question kinds the SDK does not model yet, matching
Python's raw question dictionaries and JS's pass-through. It deep clones the object and returns
an internal sealed `RawQuestion` whose `Type` is the object's `type`. The writer emits the clone
as is and never re-parents it.

Validation, as `ArgumentException` or `ArgumentNullException`: an empty questions map, a null or
empty question id, a null question, a Score with fewer than two levels or a null level, a Choice
with no options, a `FromJson` object whose `type` is missing, not a string or empty, and an
`ExtraBody` key that collides with `state`, `model` or `questions`. Only the JS SDK checks for
two Score levels; Python checks for one. Nothing else is checked client-side (section 0.1).

### 3.3 Responses and answers

```csharp
public sealed class SystemOneResponse
{
    public string Model { get; }                                     // versioned id that answered
    public IReadOnlyDictionary<string, Answer> Answers { get; }      // exactly what the server sent
    public Usage Usage { get; }
    public string? RequestId { get; }                                // null when the header is absent
    public NoulAnswer GetNoul(string questionId);
    public ChoiceAnswer GetChoice(string questionId);
    public ScoreAnswer GetScore(string questionId);
}

public sealed class Usage { public long InputTokens { get; } public long OutputTokens { get; } }
public abstract class Answer { public abstract string Type { get; } }
public sealed class NoulAnswer : Answer { public double Noul { get; } }    // probability of yes
public sealed class ChoiceAnswer : Answer
{
    public string Choice { get; }
    public double Confidence { get; }
    public IReadOnlyDictionary<string, double> Probabilities { get; }
}
public sealed class ScoreAnswer : Answer
{
    public double Score { get; }                                     // may fall between levels
    public double Confidence { get; }
    public IReadOnlyDictionary<int, JsonElement> Legend { get; }
    public IReadOnlyDictionary<int, double> Probabilities { get; }
    public int MostLikelyLevel { get; }                              // mode, for callers who need a level not a mean
}
public sealed class UnknownAnswer : Answer { public override string Type { get; } public JsonElement Raw { get; } }  // Type as sent

public static class TypeSafeModelFactory                             // for callers' unit tests
{
    public static SystemOneResponse SystemOneResponse(string model, IReadOnlyDictionary<string, Answer> answers, Usage usage, string? requestId = null);
    public static Usage Usage(long inputTokens, long outputTokens);
    public static NoulAnswer NoulAnswer(double noul);
    public static ChoiceAnswer ChoiceAnswer(string choice, double confidence, IReadOnlyDictionary<string, double> probabilities);
    public static ScoreAnswer ScoreAnswer(double score, double confidence, IReadOnlyDictionary<int, JsonElement> legend, IReadOnlyDictionary<int, double> probabilities);
    public static UnknownAnswer UnknownAnswer(string type, JsonElement raw);
    public static ModelCard ModelCard(string name, string description, string releaseDate);
    public static TypeSafeApiException ApiException(int statusCode, string? message = null, string? requestId = null, TimeSpan? retryAfter = null);  // subclass for the status
}
```

`Get*` on an id the server did not answer throws `KeyNotFoundException` listing the ids it did
answer, and on an answer of another kind throws `InvalidOperationException` naming the actual
kind. The XML docs say so in one line.

A 2xx body that is not the documented object (a gateway HTML page, an empty body, `[]`,
`"input_tokens": 1.5`) throws `TypeSafeResponseValidationException` with path `$` or the field
path. The reader wraps `JsonDocument.Parse` in `catch (JsonException)`, reads integers with
`TryGetInt64`, and treats empty Score `probabilities` as a validation error.

Request side uses mutable `JsonNode` (built in code). Response side uses immutable
`JsonElement` (read, never written). Probabilities are `double`, the wire type.

No confidence helper ships in 0.1. TypeSafe's docs say thresholds "scale with risk" and belong
to the caller, and a helper with default thresholds would encode a policy we do not own. Revisit
if users ask (section 7, P5).

### 3.4 Options and retries

```csharp
public sealed class TypeSafeClientOptions
{
    public string? ApiKey { get; set; }                              // or TYPESAFE_API_KEY
    public Uri? BaseUrl { get; set; }                                // or TYPESAFE_BASE_URL, else https://api.typesafe.ai
    public string? DefaultModel { get; set; }                        // or TYPESAFE_DEFAULT_MODEL, else jev-latest
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);   // finite, > 0
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);     // finite, > 0
    public RetryPolicy Retry { get; set; } = new RetryPolicy();
    public ILoggerFactory? LoggerFactory { get; set; }
    public override string ToString();                               // ApiKey = ***
}

public sealed class RetryPolicy
{
    public static RetryPolicy Default { get; }                       // a new instance on every read
    public static RetryPolicy None { get; }                          // new instance, MaxRetries = 0
    public int MaxRetries { get; set; } = 2;
    public bool RetryOnTimeout { get; set; } = true;                 // the double-billing escape hatch (R9)
}
public sealed class TypeSafeRequestOptions                           // null properties use the client's values
{
    public TimeSpan? AttemptTimeout { get; set; }
    public TimeSpan? TotalTimeout { get; set; }
    public RetryPolicy? Retry { get; set; }
}
```

The client copies options into an internal immutable settings object at construction and never
writes to the caller's instance (ADR-0007). Out-of-range values throw
`ArgumentOutOfRangeException` when the client is built or the call starts: a negative
`MaxRetries`, or a timeout that is zero, negative or infinite. A bad API key or base URL throws
`TypeSafeConfigurationException` (ADR-0008); the missing-key message ends with " Create a key at
https://console.typesafe.ai/keys."

The rest of the retry policy is internal constants from ADR-0005: backoff 500 ms doubling to
5 s, jitter 0.25, statuses 408, 429 and 500 to 599, `retry-after-ms` then `Retry-After`
honoured up to 60 s, and connection errors retried. `TypeSafeRequestOptions` is not called
`RequestOptions` because `System.ClientModel.Primitives.RequestOptions` is imported by most
OpenAI apps (CS0104).

### 3.5 Errors

ADR-0006 has the tree of 13 types. Every `TypeSafeApiException` carries `StatusCode`,
`RequestId`, `Endpoint`, `Body` (may contain request data, never logged by the SDK) and
`Headers` (response headers as `IReadOnlyDictionary<string, IReadOnlyList<string>>`).
`TypeSafeRateLimitException.RetryAfter` and `TypeSafeUnprocessableEntityException.ValidationErrors`
(`IReadOnlyList<ValidationError>`, a sealed class with `Location`, `Message`, `Type`) are typed.

Messages never contain customer data: they come from the body's message fields or FastAPI
`loc` and `msg` pairs only, and `input` and `ctx` are never read (ADR-0006 rule 1).

Any other exception from the handler chain, for example Polly's `TimeoutRejectedException` or
`BrokenCircuitException` from a caller's pipeline, becomes `TypeSafeConnectionException` with it
as the inner exception. Caller cancellation still wins.

XML docs list every exception a public method can throw (`AGENTS.md` rule 7).

### 3.6 Batch

```csharp
public sealed class BatchOptions { public int MaxConcurrency { get; set; } = 4; public TypeSafeRequestOptions? RequestOptions { get; set; } }
public sealed class BatchItem<TItem>
{
    public int Index { get; }                                        // position in the input
    public TItem Item { get; }
    public SystemOneResponse? Response { get; }
    public Exception? Exception { get; }                             // TypeSafeException or ArgumentException
    public TimeSpan Elapsed { get; }                                 // excludes time spent queued
    public bool Succeeded { get; }
}
```

Items are yielded in completion order. The method validates its arguments when called, not on
the first `MoveNextAsync`. Caller cancellation stops the enumeration and throws
`OperationCanceledException`, as `IAsyncEnumerable` callers expect. Breaking out of the loop
cancels in-flight calls.

Failures that are about the account, not the item, end the enumeration by throwing:
`TypeSafeAuthenticationException` (401), `TypeSafePermissionDeniedException` (403),
`TypeSafeNotFoundException` (404) and `TypeSafeConfigurationException`. Otherwise a revoked key
would turn a 10,000-item batch into 10,000 failures. Every other `TypeSafeException`, and an
`ArgumentException` from `createRequest`, comes back as a failed item.

C# 7.3 callers cannot write `await foreach`; they call `GetAsyncEnumerator()` and
`MoveNextAsync()` by hand. The README shows it.

### 3.7 Dependency injection

ADR-0009 has the surface: two extension methods, both returning `IHttpClientBuilder`.

```csharp
public static IHttpClientBuilder AddTypeSafe(this IServiceCollection services, IConfiguration configuration);
public static IHttpClientBuilder AddTypeSafe(this IServiceCollection services, Action<TypeSafeClientOptions> configure);
```

The section key `TypeSafe` binds every settable property of `TypeSafeClientOptions` except
`LoggerFactory`, which comes from the container. `Retry` binds as a nested object into the
options instance's own `RetryPolicy`. Binding uses the configuration binding source generator,
so it is AOT safe.

The named `HttpClient` gets the SDK's primary handler and an infinite handler lifetime, because
a singleton client captures one `HttpClient` and would never see the factory's handler rotation
(ADR-0009). A caller who adds their own pipeline (`AddStandardResilienceHandler()`) sets
`Retry = RetryPolicy.None` and both timeouts longer than the pipeline's total, so the SDK does
not cut it off. The README and ADR-0009 show the recipe.

### 3.8 What a user writes

```csharp
using TypeSafeSharp;

using var client = new TypeSafeClient(new TypeSafeClientOptions());  // TYPESAFE_API_KEY

var response = await client.SystemOneAsync(
    "I've been trying to connect Stripe for 3 days and it keeps failing. Please help ASAP.",
    new Dictionary<string, Question>
    {
        ["department"] = Question.Choice("Which team should handle this?", "Billing", "Technical", "Sales"),
        ["frustration"] = Question.Score("How frustrated is the customer?",
            "Calm, just stating facts", "Frustrated but civil", "Very angry"),
        ["is_urgent"] = Question.Noul("The message conveys urgency or time-sensitivity"),
    });

var department = response.GetChoice("department");
Console.WriteLine(department.Confidence >= 0.8 ? $"route to {department.Choice}" : "ask a human");
```

### 3.9 Acceptance criteria

| ID | Criterion |
| --- | --- |
| AC-3.1 | README code is copied from the sample and the sample builds against the release (runbook) |
| AC-3.2 | Request JSON for each documented example on `docs.typesafe.ai/api` matches after canonicalization (property order ignored) |
| AC-3.3 | Each documented example response deserializes to the expected typed answer, on both targets |
| AC-3.4 | Key validation rejects empty, internal whitespace, control, non-ASCII; strips outer whitespace; explicit empty does not fall back to the environment. No exception message or `ToString()` contains the key (test greps for it) |
| AC-3.6 | An answer with `"type": "ranking"` becomes `UnknownAnswer` with `Type == "ranking"` and its raw JSON; the other answers still parse |
| AC-3.7 | `EvaluateManyAsync` never has more than `MaxConcurrency` requests in flight (asserted with a counting handler), yields item failures, throws on a 401, and stops within 100 ms of cancellation |
| AC-3.8 | A host with no key in options or environment fails at host start (`StartAsync`, so `app.Run()`) with `OptionsValidationException`, not at the first request |
| AC-3.9 | With an `ActivityListener` attached, one activity per `SystemOneAsync` call carries the attributes in ADR-0010; no tag value contains the state or instruction text (test greps for it) |
| AC-3.10 | The caller's `TypeSafeClientOptions` instance is unchanged after constructing a client, and changing it afterwards does not affect the client |
| AC-3.11 | One `Question` instance used by 8 concurrent calls serializes identically each time and keeps `Parent == null`; `ExtraBody` is unchanged afterwards |
| AC-3.12 | A 422 whose `detail[].input` holds a marker string produces an exception whose `Message`, `ToString()` and `ValidationErrors` do not contain the marker |
| AC-3.13 | `Question.FromJson` on an object with an unmodelled `type` writes it back unchanged after canonicalization; the caller's object and the clone both keep `Parent == null`; a missing, non-string or empty `type` throws `ArgumentException` |
| AC-3.14 | A 2xx with an empty body, an `<html>` page, `[]` or a fractional token count throws `TypeSafeResponseValidationException` with path `$` or the field path |
| AC-3.15 | With the internal browser check forced to true, every public constructor throws `TypeSafeConfigurationException` with the message in section 3.1; a reflection test finds `[UnsupportedOSPlatform("browser")]` on the core assembly |
| AC-3.16 | The fixtures, the `Retry-After: 1.5` case and `ErrorMessage` give the same results under `tr-TR` and `de-DE`, set as both `CultureInfo.CurrentCulture` and `CurrentUICulture` |

---

## 4. Package and build

### 4.1 Layout

```
TypeSafeSharp.slnx
Directory.Build.props  Directory.Build.targets  Directory.Packages.props
global.json  NuGet.Config  .editorconfig  .gitattributes  .gitignore
LICENSE  THIRD-PARTY-NOTICES.txt  README.md  CHANGELOG.md  TypeSafeSharp.snk
CONTRIBUTING.md  SECURITY.md  CODE_OF_CONDUCT.md  AGENTS.md  CLAUDE.md
.claude/skills/writing-style/SKILL.md
.github/
  dependabot.yml  pull_request_template.md  ISSUE_TEMPLATE/
  workflows/  ci.yml  live.yml  release.yml
src/
  TypeSafeSharp/                                   ADR-0003 folders, PACKAGE.md
    PublicAPI.Shipped.txt  PublicAPI.Unshipped.txt
  TypeSafeSharp.Extensions.DependencyInjection/    PACKAGE.md
    PublicAPI.Shipped.txt  PublicAPI.Unshipped.txt
tests/
  TypeSafeSharp.Tests/                             net10.0;net481
    Fixtures/                                      documented request and response JSON
  TypeSafeSharp.Extensions.DependencyInjection.Tests/
  contract/openapi.snapshot.json
  packaging/verify-package.sh  consumers/
docs/
  PLAN.md  IMPLEMENTATION.md  adr/  runbooks/release.md
scripts/changelog-section.sh
```

Copy from ada-csharp unchanged: `.gitattributes`, `NuGet.Config`, `global.json`,
`CODE_OF_CONDUCT.md`, `scripts/changelog-section.sh`, the writing-style skill.

Adapt, because they carry Ada-specific content: `.editorconfig` (drop the P/Invoke rules, add
the multi-target block in section 4.3), `CONTRIBUTING.md` (drop CMake and C++ setup),
`SECURITY.md` (send service bugs and leaked keys to TypeSafe), issue templates (ask for
`x-typesafe-request-id`, never the key or real state), `dependabot.yml` (groups for
`Microsoft.Extensions.*` and `System.*`, `cooldown`, the `dotnet-sdk` ecosystem),
`Directory.Build.props`, the workflows, the PR template and the runbook.

### 4.2 Dependencies

The only canonical dependency list. ADRs link here.

| Package | Version | Where | Why |
| --- | --- | --- | --- |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.12 | core, both targets | ADR-0010. Brings `Microsoft.Extensions.DependencyInjection.Abstractions` transitively. |
| `System.Text.Json` | 10.0.12 | core, ns2.0 | ADR-0004 |
| `System.Diagnostics.DiagnosticSource` | 10.0.12 | core, ns2.0 | ADR-0010 |
| `Microsoft.Bcl.TimeProvider` | 10.0.12 | core, ns2.0 | testable time (Q6, closed). Its only dependency is `Microsoft.Bcl.AsyncInterfaces`. |
| `Microsoft.Bcl.AsyncInterfaces` | 10.0.12 | core, ns2.0 | `IAsyncEnumerable` is in the public API, so it is referenced directly |
| `Microsoft.Extensions.Http` | 10.0.12 | DI package | ADR-0009 |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | 10.0.12 | DI package | ADR-0009, with the binding source generator |
| `PolySharp` | 1.16.0 | build only, private | language features and attributes on ns2.0 |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | 5.6.0 | build only | public surface tracking |
| `xunit.v3` | 4.0.1 | tests | ADR-0012 |
| `Microsoft.Testing.Extensions.CodeCoverage` | 18.11.2 | tests | coverage |
| `Microsoft.Testing.Extensions.TrxReport` | 2.4.1 | tests | CI reports |
| `Microsoft.Extensions.TimeProvider.Testing` | 10.10.0 | tests | `FakeTimeProvider` |
| `Microsoft.Extensions.Hosting` | 10.0.12 | DI tests only | `HostApplicationBuilder` for AC-3.8 |
| `Microsoft.NETFramework.ReferenceAssemblies` | 1.0.3 | tests, net481 leg | build net481 on any OS |

Versions checked on nuget.org 2026-09-24, rechecked 2026-10-06, and pinned in `Directory.Packages.props`.
`CentralPackageTransitivePinningEnabled` is off, because pinning a transitive package promotes
it into the nuspec, and with it off the nuspec lists exactly the references reviewed here
(AC-4.2). SourceLink comes from the .NET 10 SDK, so no `Microsoft.SourceLink.GitHub` reference
is needed.

### 4.3 Build settings

`Directory.Build.props`, delta from ada-csharp.

Keep: `LangVersion latest`, `Nullable enable`, `TreatWarningsAsErrors`,
`EnforceCodeStyleInBuild`, `AnalysisLevel latest-all`, `Deterministic`,
`ContinuousIntegrationBuild` on CI, `PublishRepositoryUrl`, `EmbedUntrackedSources`,
`NuGetAudit` with `NuGetAuditMode all` (transitive packages are where advisories hide),
`CS1591` as error.

Change:

```xml
<!-- Libraries and tests set TargetFrameworks themselves (ADR-0001). Remove from ada-csharp:
     TargetFramework, InvariantGlobalization (an app setting), AdaUrlUpstreamTag. -->
<Version>0.1.0</Version>                                    <!-- one version for both packages, ADR-0011 -->
<AssemblyVersion>0.0.0.0</AssemblyVersion>                   <!-- major only, ADR-0013 -->
<Product>TypeSafeSharp</Product>
<Company>TypeSafeSharp contributors</Company>
<Copyright>Copyright (c) TypeSafeSharp contributors</Copyright>
<RepositoryUrl>https://github.com/sanamhub/typesafe-dotnet</RepositoryUrl>
<SignAssembly>true</SignAssembly>
<AssemblyOriginatorKeyFile>$(MSBuildThisFileDirectory)TypeSafeSharp.snk</AssemblyOriginatorKeyFile>
<!-- High and critical advisories fail restore; moderate and low stay warnings (org rule 6). -->
<WarningsNotAsErrors>NU1901;NU1902</WarningsNotAsErrors>
```

`src/TypeSafeSharp/TypeSafeSharp.csproj`:

```xml
<PropertyGroup>
  <TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
  <IsAotCompatible Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">true</IsAotCompatible>
  <!-- Supplies [UnsupportedOSPlatform] and the trim attributes on ns2.0 (ADR-0001) -->
  <PolySharpIncludeRuntimeSupportedAttributes>true</PolySharpIncludeRuntimeSupportedAttributes>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <EnablePackageValidation>true</EnablePackageValidation>
  <IsPackable>true</IsPackable>
  <PackageId>TypeSafeSharp</PackageId>
  <Description>Unofficial .NET client for TypeSafe AI's System One API and the Jev model. Typed Noul, Choice and Score questions, retries matching the official SDKs, netstandard2.0 and NativeAOT.</Description>
  <PackageTags>typesafe;jev;system-one;ai;llm;classification;decision;guardrails</PackageTags>
  <PackageProjectUrl>https://github.com/sanamhub/typesafe-dotnet</PackageProjectUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>PACKAGE.md</PackageReadmeFile>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>
<ItemGroup>
  <None Include="PACKAGE.md" Pack="true" PackagePath="\" />   <!-- without it pack fails with NU5039 -->
</ItemGroup>
```

The DI project repeats the same shape with its own `PackageId`, `Description` and `PACKAGE.md`,
plus `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`.
`PACKAGE.md` files use absolute links only, since nuget.org renders them outside the repo. The
root `README.md` is for GitHub.

`.editorconfig` for `src/**.cs` sets these rules to `none`, with this justification in the file:
on `net10.0` they suggest APIs that `netstandard2.0` lacks, and following them would need `#if`
for code that behaves the same on both targets. Found by building the reference helpers.

| Rules | Suggestion that ns2.0 cannot follow |
| --- | --- |
| CA1510, CA1511, CA1512, CA1513 | `ArgumentNullException.ThrowIfNull` and similar throw helpers |
| CA1845, CA1846 | span-based `string.Concat`, `AsSpan` |
| CA1847, CA1865, CA1866, CA1867 | `char` overloads of `Contains`, `StartsWith`, `EndsWith`, `IndexOf` |
| CA1849 | `CancellationTokenSource.CancelAsync` |
| CA2249 | `string.Contains(string, StringComparison)` |

`CA2007` (`ConfigureAwait(false)`) stays an error in `src`, because ns2.0 callers include
WinForms, WPF and classic ASP.NET. `CA2000` stays on.

### 4.4 Acceptance criteria

| ID | Criterion |
| --- | --- |
| AC-4.1 | `dotnet build -c Release` has no warnings on either target; that is the lint gate |
| AC-4.2 | Release runbook reviewer check: open both `.nupkg` files from the `verify` artifact; the `net10.0` group of the core package lists only `Microsoft.Extensions.Logging.Abstractions`, the `netstandard2.0` group adds only the four packages in section 4.2, and the DI package lists only its section 4.2 rows plus the core package |
| AC-4.3 | A NativeAOT console consuming the `.nupkg` with `<TrimmerRootAssembly Include="TypeSafeSharp" />` publishes with warnings as errors and zero IL2xxx or IL3xxx warnings, and its stubbed call with a plain Choice question passes. An AOT web app calling `AddTypeSafe(IConfiguration)` publishes the same way |
| AC-4.4 | A `net481` console consuming the `.nupkg`, at `LangVersion 7.3` and with no `System.Net.Http` reference, compiles `new TypeSafeClient(options)`, sets `SystemOneRequest.Model` and `RetryPolicy.MaxRetries`, and runs the stubbed call |
| AC-4.5 | `PublicAPI.Unshipped.txt` is empty at every release tag (all moved to Shipped) |
| AC-4.6 | No `#if` other than `#if NET` in `src`, checked by CI grep (ADR-0001) |
| AC-4.7 | Both `.nupkg` file names carry the tag's version, checked in the release `verify` job |
| AC-4.8 | Both assemblies are strong-named with the checked-in key and have `AssemblyVersion` `<major>.0.0.0` (ADR-0013) |

---

## 5. Test strategy

ADR-0012 has the rules. This section is the checklist.

### 5.1 Unit tests (stub handler, both targets)

| Area | Cases |
| --- | --- |
| Serialization | each question type; open values as string, object, array, null; `ExtraBody` merge and collisions; `Model` default and override; shared `Question` across concurrent calls (AC-3.11) |
| `FromJson` | round trip of an unmodelled type; caller's object and clone keep `Parent == null`; missing, non-string and empty `type` (AC-3.13) |
| Deserialization | each documented answer; `type` not first; missing required fields name the JSON path; unknown kind; malformed `legend` key; non-numeric `noul`; empty `answers`; `Get*` on an unanswered id and on the wrong kind |
| Non-JSON 2xx | empty body, `<html>`, `[]`, fractional token count, empty Score `probabilities` (AC-3.14) |
| Validation | every rule in sections 3.2 and 3.4 |
| Configuration | precedence code over env over default; whitespace env ignored; explicit empty key; each key rule; `http://` base rejected except loopback; options snapshot (AC-3.10) |
| Errors | each status to its type; FastAPI `detail` flattening; `input` never leaks (AC-3.12); JSON body without message fields; plain-text body capped at 200; `RequestId` present and absent; `HttpClient.Timeout` maps to `TypeSafeTimeoutException` |
| Handler-chain exceptions | `TimeoutRejectedException` and `BrokenCircuitException` from a caller's handler become `TypeSafeConnectionException` with the inner exception; caller cancellation still throws `OperationCanceledException` |
| Headers and HTTP | every header in section 3.1, including the `X-TypeSafe-Runtime` format; retry count on retries only; `ExpectContinue` false; HTTP/2 requested on `net10.0`; a 302 is not followed and becomes `TypeSafeApiException` |
| Browser guard | forced browser check throws from every public constructor; assembly attribute present (AC-3.15) |
| Dispose during a call | owned client disposed mid-call: `ObjectDisposedException`, no retry |
| Culture | fixtures, `Retry-After: 1.5` and `ErrorMessage` under `tr-TR` and `de-DE` (AC-3.16) |
| Logging | event ids and levels (event 5, failed after retries, is `Warning`); no header or body logged at any level |
| Telemetry | span attributes present; no content in any tag |
| Batch | concurrency bound, completion order, `Index`, fatal errors throw, item errors yield, cancellation, early break cancels in-flight calls |
| DI | binding (including nested `Retry`), `ValidateOnStart` with and without the env var, singleton lifetime, named client handler settings |

### 5.2 Retry table (one theory, one row each)

| Scenario | Expect |
| --- | --- |
| 429 then 200 | 2 attempts, second has `X-TypeSafe-Retry-Count: 1` |
| 429 with `retry-after-ms: 250` | waits 250 ms |
| 429 with `Retry-After: 2` | waits 2 s |
| 429 with `Retry-After: <HTTP date 3 s ahead>` | waits about 3 s |
| 429 with `Retry-After: 120` | ignores it (over 60 s cap), uses backoff |
| 529 three times | 3 attempts, `TypeSafeOverloadedException` |
| 500, 502, 503, 504, 408 | retried |
| 400, 401, 403, 404, 422 | not retried |
| connection reset then 200 | retried |
| attempt timeout then 200 | retried when `RetryOnTimeout`, `TypeSafeTimeoutException` when exhausted |
| backoff sequence | 500 then 1000 ms, minus up to 25% jitter, capped at 5 s |
| total budget 1 s, backoff 2 s | no retry, last error thrown |
| total budget shorter than attempt timeout | attempt cut at the budget, `TypeSafeTimeoutException` |
| caller cancels during delay | `OperationCanceledException`, no further attempt |
| `RetryPolicy.None` | 1 attempt |

Time comes from `TimeProvider`, and tests drive it with `FakeTimeProvider`, so the table runs in
milliseconds. Jitter comes from an injected source, so delays are exact in tests.

### 5.3 Acceptance criteria

| ID | Criterion |
| --- | --- |
| AC-5.1 | Line coverage on `src/` is collected on the Linux leg and shown in the job summary. 80% is a target, not a gate (org rule 7) |
| AC-5.2 | Every row in section 5.2 exists as a test case |
| AC-5.3 | Unit tests pass on `net10.0` (Linux, Windows) and `net481` (Windows); each leg passes `--minimum-expected-tests`, set through `TestingPlatformCommandLineArguments` in each test csproj, so none can pass by running nothing |
| AC-5.4 | Packaging tests AC-4.3 and AC-4.4 run on every PR |
| AC-5.5 | Live suite passes against `jev-latest` in `live.yml`: weekly, on manual dispatch, and green within 24 hours before each tag (runbook). Synthetic text only; the key is an environment secret |
| AC-5.6 | OpenAPI drift check runs nightly and fails on any change to the snapshot; GitHub mails the owner |

---

## 6. CI and release

### 6.1 Workflows

| File | Trigger | Jobs | From ada-csharp |
| --- | --- | --- | --- |
| `ci.yml` | PR, push to main | `build-test` (ubuntu-24.04 net10.0; windows-2022 net10.0 and net481; a `no-if` grep step for AC-4.6; coverage summary on Linux), `packaging` (AC-4.3, AC-4.4) | `ci.yml` minus natives, arm, musl and macOS legs, System.Uri report |
| `live.yml` | nightly cron, manual | `openapi-drift` nightly with no secret (AC-5.6); `live-tests` weekly and on manual dispatch, in environment `live` (AC-5.5) | new |
| `release.yml` | tag `v*`; manual is dry run only | `preflight`, `verify`, `publish` (environment `production`, trusted publishing, provenance) | `release.yml` minus natives, checksums, SBOM, signing; builds once |

Every action is pinned to a full SHA with a version comment. `permissions: contents: read` at
the top, widened per job. `persist-credentials: false` on every checkout that does not push.

CodeQL runs through GitHub's default setup, not a workflow job; enabling both makes GitHub
reject the workflow's results. NuGetAudit (NU1903 and NU1904 as errors) fails restore on a
vulnerable package. `ci.yml` also has ada-csharp's `deps` job on pull requests
(`actions/dependency-review-action`, `fail-on-severity: high`), which covers action versions and
licences that NuGetAudit does not see (org rule 6).

The live key is the only secret besides `NUGET_USER`. It sits in environment `live`, which
deploys from `main` only, so no pull request run can read it. `live-tests` never runs on
`pull_request`. The key is a dedicated one for CI, separate from the maintainer's own, so it can
be revoked alone.

### 6.2 Release pipeline

`preflight` fails in seconds unless the tag, `<Version>` in `Directory.Build.props` and the
`CHANGELOG.md` section for that version agree, and `PublicAPI.Unshipped.txt` is empty in both
projects.

`verify` packs both packages once, checks both file names carry the tag version, runs the
packaging consumers against those files, and uploads them as an artifact.

`publish` runs on ubuntu-24.04 in environment `production`:

1. Download exactly the files `verify` tested. Never re-pack; ada-csharp re-packs, and this
   plan fixes that.
2. Log in with `NuGet/login` (OIDC).
3. Push the core package, then the DI package, each with `--skip-duplicate`. A failure then never
   leaves a DI package on nuget.org without its dependency.
4. Attest provenance with `actions/attest` (`id-token: write`, `attestations: write`).
5. Create the GitHub release from the changelog section.

The whole `publish` job has `if: github.event_name == 'push'`, so a `workflow_dispatch` run
stops after `verify` and is always a dry run. (A step-level condition would not work: the
`production` environment only deploys from `v*` tags, so the job would be refused on `main`.)
`concurrency: release-${{ github.ref }}` without cancelling.

Post-deploy verification (org rule 12) is a runbook step, not a workflow: build the sample
against the nuget.org version and run Quickstart once with the maintainer's key.

### 6.3 One-time setup

1. Create `sanamhub/typesafe-dotnet` (public). Enable dependency graph, Dependabot security
   updates, CodeQL default setup, private vulnerability reporting, secret scanning and push
   protection, as ada-csharp has them.
2. Environment `production`: deploys from tags `v*` only, with the maintainer as required
   reviewer.
3. A repository ruleset that restricts who can create or delete `v*` tags.
4. nuget.org, Account, Trusted Publishing: owner `sanamhub`, repository `typesafe-dotnet`,
   workflow `release.yml`, environment `production`, package scope `TypeSafeSharp*`. Scope the
   existing ada-csharp policy to `Ada.Url` the same way. Add secret `NUGET_USER`.
5. Environment `live`: deploys from `main` only, no required reviewer (a reviewer would stall
   the schedule), environment secret `TYPESAFE_API_KEY` holding a dedicated CI key.
6. Optional: email TypeSafe with the use case (open-source .NET client, about ten small requests
   a week from CI, synthetic text) and ask for a higher limit or test credits on the CI key.
   Nothing waits on the answer.
7. Set `<Version>0.1.0-alpha.1</Version>`, run the release dry run, then push `v0.1.0-alpha.1`
   to reserve both IDs (ADR-0002).

### 6.4 Runbook

[docs/runbooks/release.md](runbooks/release.md), adapted from ada-csharp. Before tagging: a
green `live-tests` run within 24 hours, dispatched by hand if needed (AC-5.5), a green
`openapi-drift` run this week (R10), and changelog
parity against the official SDKs. Before approving `production`: the dependency review in AC-4.2.
After publishing: the sample build and Quickstart run (AC-3.1, section 6.2), and rollback for two
packages without a long-lived key if either fails.

---

## 7. Phases and roadmap

Each phase ends with its ACs green. Estimates are for one developer who knows the conventions,
with AI assistance, and include review time. [IMPLEMENTATION.md](IMPLEMENTATION.md) breaks each
phase into tasks T00 to T18.

| Phase | Tasks | Scope | Exit | Estimate |
| --- | --- | --- | --- | --- |
| P0 | T00 to T02 | Human prerequisites, repo skeleton from ada-csharp templates, props, strong-name key, CI green on a smoke test, ADRs approved | AC-4.1, AC-4.6, AC-4.8 | 0.5 day |
| P1 | T03 to T10 | Exceptions and guards, questions with `FromJson`, request writer, response reader with non-JSON 2xx, options and key, error mapping, transport and `/v1/models` (browser guard, `ExpectContinue`, redirects, runtime header, dispose flag, handler-chain exceptions), AOT and net481 packaging consumers | AC-3.2 to AC-3.4, AC-3.6, AC-3.10 to AC-3.16, AC-4.3, AC-4.4, AC-5.4 | 2.5 days |
| P2 | T11, T12 | Retry loop and timeouts; logging and tracing (events 1 to 5, span only) | AC-3.9, AC-5.2, AC-5.3 | 1.5 days |
| P3 | T13 to T16 | Batch, DI package with AotWeb consumer, READMEs, drift check and scheduled live tests | AC-3.7, AC-3.8, AC-5.5, AC-5.6 | 1.5 days |
| P4 | T17, T18 | Release pipeline dry run, `0.1.0-alpha.1`, feedback, then `0.1.0` | AC-3.1, AC-4.2, AC-4.5, AC-4.7, runbook end to end | 1 day plus soak |
| P5 (post 0.1) | n/a | Candidates, each needs a user asking: `TypeSafeSharp.Extensions.AI` (a guardrail `DelegatingChatClient` first, an `IEvaluator` adapter second); enum-typed Choice; keyed DI; `Meter` and token metrics with `IMeterFactory`; per-call headers; raw response access through our own immutable type; `response_model` style mapping via `JsonTypeInfo<T>`; confidence helpers; API key rotation; a shared 429 pause across batch workers; an `IAsyncEnumerable` batch source | per feature | n/a |

AC-5.1 is reported from P0 on and gates no phase. An `AIFunction` bridge is not on the P5 list.
It is three lines with `AIFunctionFactory.Create`, and TypeSafe positions System One as code
that stays in control, not as an agent tool.

### 7.1 Exit criteria for 1.0.0

- Three months on nuget.org with no open bug labelled `breaking`.
- At least one known production consumer outside the author.
- Public API reviewed against the Azure .NET guidelines checklist, and every deviation has an ADR.
- A package validation baseline (`PackageValidationBaselineVersion`) set to the last 0.x
  release, so an unplanned break fails pack.
- `AssemblyVersion` moves to `1.0.0.0` once and stays there for 1.x (ADR-0013).

---

## 8. Risk register

| ID | Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- | --- |
| R1 | TypeSafe ships an official .NET SDK | medium | high | Deprecate this package on nuget.org pointing to it (README "Versioning and support"). Our type names already match theirs, so migration is a namespace change. |
| R2 | Another breaking API change | high | medium | `UnknownAnswer`, `Question.FromJson`, nightly drift check, minor bump with changelog (ADR-0011) |
| R3 | TypeSafe objects to the name | low | medium | Fallback `Sanamhub.TypeSafe` (ADR-0002). Before the first publish it is a rename; after it, a new ID and a deprecated old one. |
| R4 | Rate limits tighten and live tests flake | medium | low | Live suite runs weekly and before each tag, never on PRs; about ten small requests a run; optional quota request to TypeSafe (section 6.3) |
| R5 | Community packages close our gaps first | high | medium | Revisit at P3 (section 2.4); contribute instead if so |
| R6 | Customer data leaks through logs, spans or exception text | low | high | ADR-0006 rule 1, ADR-0008, AC-3.4, AC-3.9, AC-3.12 greps |
| R7 | `netstandard2.0` asset breaks on .NET Framework without anyone noticing | medium | high | `net481` unit and packaging legs on every PR, C# 7.3 consumer |
| R8 | GenAI semantic conventions rename attributes | high (happened 2026-09-22) | low | Follow the `semantic-conventions-genai` repository and note each rename in the changelog (ADR-0010) |
| R9 | Retried timeouts double bill | low | low | README Limits; `RetryOnTimeout = false` for callers who care; adopt an idempotency key if TypeSafe adds one |
| R10 | GitHub disables the nightly drift schedule after 60 days without repository activity | medium | medium | Runbook step before each tag: check `live.yml` still ran this week |
| R11 | Strong-name key or `AssemblyVersion` policy changed after publish | low | high | ADR-0013, AC-4.8; never change the key |

---

## 9. Open questions

| ID | Question | Default if unanswered |
| --- | --- | --- |
| Q1 | Package name: `TypeSafeSharp`, or author-prefixed `Sanamhub.TypeSafe`? | Closed 2026-10-06: `TypeSafeSharp` (ADR-0002); bare `TypeSafe` rejected as the vendor's likely ID |
| Q2 | Build our own, or first offer fixes to `TypeSafe.AI.Sdk` (the only ns2.0 package)? | Build, revisit at P3 |
| Q3 | Is personal GitHub (`sanamhub`) the right owner, or should this sit under an org? | Closed 2026-09-25: `sanamhub`, personal OSS with one maintainer, like ada-csharp |
| Q4 | Do TypeSafe's API terms of service allow redistribution of a client and use of the name? | Closed 2026-09-25, maintainer decision: an unofficial client needs no confirmation. Unofficial status is stated in the NuGet description, the README's first line and a disclaimer at its end (ADR-0002). R3 covers an objection. |
| Q5 | Test OpenRouter and Vercel gateways? Needs keys for both. | Document only |
| Q6 | `TimeProvider` or an internal seam? | Closed: `TimeProvider` (section 4.2) |
| Q7 | Does commit attribution follow the repo's writing-style rule (no AI trailers) or the AI tool's default trailer? | Repo rule, stated in `AGENTS.md` |

---

## 10. Notes for the implementer

- Start with [IMPLEMENTATION.md](IMPLEMENTATION.md). It orders the work into tasks with done
  criteria and gives compile-checked code for the parts that are easy to get wrong.
- Read `docs.typesafe.ai/api.md` and the two official SDK sources before P1. `typesafe-sdk-js`
  `src/retry.ts`, `src/errors.ts`, `src/questions.ts` and `src/client.ts` are 800 lines together
  and settle most edge cases. Cite them in comments where a rule is ported, with the tag.
- Write the fixtures first. The documented request and response pairs in the API reference are
  the spec.
- Keep `internal` everything that is not in section 3. `PublicAPI.Unshipped.txt` will show any
  slip in review.
- Never paste the real API key into a test, a fixture, an issue, or an AI prompt. Tests use
  `ts_test_0000000000000000`. The live suite reads it from the environment only.
- State text in fixtures and live tests is synthetic. No customer tickets, even redacted.
- Follow the writing-style skill for every doc, comment and commit.
