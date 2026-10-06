# ADR-0003: Flat layout instead of four Clean Architecture projects

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06
- **Relates to:** org standard section 5 (Clean Architecture by default, exceptions approved by name)

## Context

The org standard is Clean Architecture by default: Domain, then Application, then
Infrastructure, then Presentation. A deviation has to be written down and approved by name.

This package is an HTTP client for two endpoints, `POST /v1/systemone` and `GET /v1/models`.
The public surface is about thirty types plus the exception family: two clients, client and
per-call options, question and answer types, request and response models, a retry policy, batch
types, and a model factory for tests. There is no persistence, no business rule we own, and no dependency to
invert. The one piece of infrastructure is an `HttpClient` call wrapped in a retry loop.

The official JavaScript and Python SDKs are both single-package, flat layouts of about ten
source files. See `docs/PLAN.md` section 2.

## Decision

One library project, flat folders:

```
src/TypeSafeSharp/
  TypeSafeClient.cs            System One calls and batches
  TypeSafeClientOptions.cs
  ModelsClient.cs              GET /v1/models
  TypeSafeRequestOptions.cs    per-call overrides
  RetryPolicy.cs
  TypeSafeModelFactory.cs      responses, answers and exceptions for tests
  Questions/                   Question, NoulQuestion, ChoiceQuestion, ScoreQuestion,
                               internal RawQuestion (ADR-0004)
  Answers/                     Answer, NoulAnswer, ChoiceAnswer, ScoreAnswer, UnknownAnswer
  Models/                      SystemOneRequest, SystemOneResponse, Usage, ModelCard
  Batch/                       BatchOptions, BatchItem<TItem>
  Errors/                      TypeSafeException and subclasses, ValidationError
  Http/                        internal: retry loop, header building, request id
  Serialization/               internal: RequestWriter, ResponseReader (no JsonSerializerContext)
  Diagnostics/                 internal: ActivitySource, log messages
```

`Http/`, `Serialization/` and `Diagnostics/` hold only `internal` types. That is the whole
layering rule. It holds through `PublicAPI.Shipped.txt` (a new public type fails the build until
it is listed) rather than through a project boundary.

The optional dependency injection package (ADR-0009) is a second project because it carries
dependencies the core must not force on anyone, not because it is a layer.

## Consequences

One assembly for consumers to reference, no artificial internal seams, and a source tree a
reader can hold in their head. Follows YAGNI.

If the API grows real client-side logic (for example a local rules engine over answers), the
split can be introduced then. For a library of about thirty types that refactor is cheap, and
paying for it up front is paying for a guess.

## Alternatives considered

**Four projects** (`.Domain`, `.Application`, `.Infrastructure`, `.Presentation`). Rejected.
Four assemblies and three project references to express one HTTP call. Consumers would also see
four packages or one package with four DLLs.

**One project, four folders named after the layers, plus an architecture test.** Rejected. The
folder names would describe layers that do not exist here, and the test would prove a rule the
`internal` keyword already enforces.
