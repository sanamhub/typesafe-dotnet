# ADR-0010: Logging and tracing through the .NET abstractions, metrics later

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06
- **Relates to:** org standard section 9 (monitoring and alerting mandatory in production)

## Context

Callers run this SDK inside services that already export OpenTelemetry. A decision call that
adds 100 ms to a request should show up as a child span with the model and token counts on it,
without the caller writing a handler.

The official SDKs log through a console-compatible logger (JS) or the `typesafe_sdk` logger
(Python), filtered by `TYPESAFE_LOG_LEVEL`. `TypeSafe.AI.Sdk` wraps
`Microsoft.Extensions.Logging` in its own filter. Its default level is `Warning`, but it never
logs at `Warning` or `Error`, so it is silent unless the caller sets both the host level and
`TYPESAFE_LOG_LEVEL`. Of the .NET packages reviewed, only `Jev.Net` emits traces and metrics,
under its own names rather than the GenAI conventions (see PLAN.md section 2.3).

## Decision

1. **Logging:** `ILogger` from `Microsoft.Extensions.Logging.Abstractions`, category
   `TypeSafeSharp`. Options take an `ILoggerFactory`; the default is `NullLoggerFactory`. All
   messages use `[LoggerMessage]` source generation with stable event ids. `{Endpoint}` is
   `POST /v1/systemone` or `GET /v1/models`, since model listing is logged too:

   | Id | Level | Message |
   | --- | --- | --- |
   | 1 | Debug | `{Endpoint} attempt {Attempt} started` |
   | 2 | Information | `{Endpoint} {StatusCode} in {ElapsedMs} ms, request {RequestId}` |
   | 3 | Warning | `Retrying {Endpoint} after {Reason}, retry {Retry} of {MaxRetries}, waiting {DelayMs} ms` |
   | 4 | Warning | `Unknown answer kind {Kind} for question {QuestionId}; returned as UnknownAnswer` |
   | 5 | Warning | `{Endpoint} failed after {Attempts} attempts: {StatusCode}, request {RequestId}` |

   Event 5 is `Warning`, not `Error`, because the caller decides what is an error. The web
   sample turns a 429 into a 503 on purpose, and that should not page anyone. Headers and bodies
   are never logged (ADR-0008).

   `TYPESAFE_LOG_LEVEL` is **not** read. In .NET the host owns filtering
   (`Logging:LogLevel:TypeSafeSharp`), and a second filter is how `TypeSafe.AI.Sdk` ended up
   silent by default. The README lists this as a deliberate difference from the official SDKs.
2. **Tracing:** one `ActivitySource` named `TypeSafeSharp` with the package version, one
   `Client` activity per `SystemOneAsync` call (not per attempt), named `systemone {model}`.
   Attributes follow the OpenTelemetry GenAI semantic conventions where a key exists and use a
   `typesafe.` prefix where none does:

   | Attribute | Value |
   | --- | --- |
   | `gen_ai.provider.name` | `typesafe` |
   | `gen_ai.operation.name` | `systemone` |
   | `gen_ai.request.model` | as sent, for example `jev-latest` |
   | `gen_ai.response.model` | as returned, for example `jev-1.13.0` |
   | `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens` | from `usage` |
   | `server.address`, `server.port` | from `BaseUrl` |
   | `http.response.status_code` | final status |
   | `error.type` | exception type name or status code, on failure |
   | `typesafe.request_id` | `x-typesafe-request-id` |

   Content (state, instructions, criteria, answers) is never recorded (ADR-0008).
3. **Metrics are deferred to P5.** On 2026-09-22 the GenAI conventions moved to
   `https://github.com/open-telemetry/semantic-conventions-genai`, and the move replaced the
   `gen_ai.client.token.usage` histogram with token counters. Instrument names are a public
   contract once dashboards depend on them, so 0.1 waits for them to settle. The span already
   carries the token counts and the outcome.
4. **Zero cost when unused.** `ActivitySource.StartActivity` returns `null` with no listener and
   the SDK checks `Activity is not null` before building tags.
5. **The GenAI conventions are still marked Development in OpenTelemetry**, and they already
   changed once while this ADR was being written. We follow the current names and note every
   rename in the changelog (PLAN.md section 8, R8).

## Consequences

A caller adds `.WithTracing(t => t.AddSource("TypeSafeSharp"))` to
`builder.Services.AddOpenTelemetry()`. Token usage then appears on each call's span without
custom code. Callers who need token totals on a dashboard aggregate them from spans until
metrics ship.

On `netstandard2.0` this adds `System.Diagnostics.DiagnosticSource`. On `net10.0` it is in the
box.

## Alternatives considered

**A custom `ITypeSafeLogger` with a console default.** Rejected. It duplicates
`Microsoft.Extensions.Logging` and forces an adapter on every host that already has one.

**One span per attempt.** Rejected for 1.x. The HTTP client instrumentation already gives
per-attempt spans to anyone who enables it, and log event 3 records each retry.

**Ship the metrics now under the current names.** Rejected for the reason in decision 3. When
they ship, a static `Meter` comes first; `IMeterFactory` only if asked.
