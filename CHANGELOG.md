# Changelog

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- `AddTypeSafe` now reports the specific key problem at host start (whitespace inside the key, a
  control character, or a non-ASCII character) instead of the generic missing-key message.
- The invalid-timeout message now says "at most 24.86 days" instead of the rounded "24 days".

## [0.1.0] - 2026-10-06

First stable release, same surface as the `0.1.0-alpha.1` prerelease. Checked against API
0.2.0, `typesafe-sdk-js` 0.6.0 and `typesafe-sdk-python` 0.7.2.

### Added

- `TypeSafeClient` for `POST /v1/systemone` and `GET /v1/models`, on `netstandard2.0` and
  `net10.0`, callable from C# 7.3. One client per app; it is thread-safe. ADR-0001, ADR-0007.
- Noul, Choice and Score questions with typed answers, and `Question.FromJson` for question kinds
  this package does not model yet. An unknown answer kind comes back as `UnknownAnswer` with its
  raw JSON instead of failing the call. ADR-0004.
- Retries matching the official SDKs: 2 retries on 408, 429 and 5xx and on connection errors,
  backoff from 500 ms to 5 s, `retry-after-ms` then `Retry-After` honoured up to 60 s, and a
  30 s budget for the whole call. Retried timeouts can bill twice; `RetryOnTimeout = false` turns
  them off. ADR-0005.
- One exception type per common status, each with `RequestId`. Messages never carry the key or
  request data, including the `input` a 422 echoes back. ADR-0006, ADR-0008.
- `EvaluateManyAsync` for many requests with bounded concurrency, results as they finish. A
  rejected key ends the batch instead of failing every item. ADR-0007.
- `TypeSafeSharp.Extensions.DependencyInjection` with `AddTypeSafe`, NativeAOT-safe binding, and
  the key checked at host start. ADR-0009.
- `ILogger` events 1 to 5 and one OpenTelemetry span per call with the GenAI attributes. No
  content is recorded. Metrics wait for the GenAI conventions to settle. ADR-0010.
- HTTP/2 on `net10.0`, falling back to 1.1, so a batch shares one connection.
- Strong-named assemblies, `AssemblyVersion` `0.0.0.0` for all of 0.x. ADR-0013.
