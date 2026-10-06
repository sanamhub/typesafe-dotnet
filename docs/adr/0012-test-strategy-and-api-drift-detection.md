# ADR-0012: Test strategy and API drift detection

- **Status:** accepted
- **Date:** 2026-09-24, amended 2026-09-25 (live tests in CI)
- **Approved by:** Sanam, 2026-10-06
- **Relates to:** org standard section 7 (test pyramid, 80% coverage target, no real PII)

## Context

Most of the SDK is deterministic: serialization, validation, retry decisions, exception mapping.
That is unit test territory with a stub `HttpMessageHandler`. A small part only fails against the
real service: auth, the actual JSON the server sends, and rate-limit headers.

`TypeSafe.AI.Sdk` tests only on `net10.0`, so its `netstandard2.0` build, the one .NET Framework
users load, never runs in CI. Its live tests skip unless a key is present, so nothing says
whether anyone ran them.

The API has already broken its SDKs once in its first two weeks (Score criteria from map to
array). We want to hear about the next one from CI, not from a user.

## Decision

1. **xUnit v3 on Microsoft.Testing.Platform**, coverage with
   `Microsoft.Testing.Extensions.CodeCoverage`, as in ada-csharp. Coverage is collected and shown
   in the job summary. 80% for `src/` is a target, as org standard section 7 says, not a gate.
   CI passes `--filter-not-trait "Category=Live"`, and each test project sets
   `--minimum-expected-tests <n>` through `TestingPlatformCommandLineArguments`, so the `net481`
   leg cannot pass by running nothing.
2. **No mocking library.** One `StubHandler : HttpMessageHandler` in the test project scripts
   responses and records requests. Time comes from `FakeTimeProvider`
   (`Microsoft.Extensions.TimeProvider.Testing`), passed with a jitter source through the
   internal constructor that `InternalsVisibleTo` exposes to tests. The JSON fixtures are the
   documented examples from `docs.typesafe.ai/api` plus hand-built edge cases, all with
   synthetic text.
3. **Run the tests on both targets.** The test project targets `net10.0;net481`. The PR matrix
   runs `net10.0` on `ubuntu-24.04`, and `net10.0` and `net481` on `windows-2022`, where
   `net481` loads the `netstandard2.0` build. There is no macOS leg.
4. **Categories:** unit tests have no trait. `[Trait("Category", "Live")]` tests call the real
   API. They never run on pull requests. `live.yml` job `live-tests` runs them weekly and on
   manual dispatch, reading `TYPESAFE_API_KEY` from the secret of environment `live`, which
   deploys from `main` only. The key is a dedicated CI key, not the maintainer's own. The runbook
   requires a green run within 24 hours before each tag. A run sends about ten requests of a few
   hundred tokens, well under one US cent at $0.042 per million input tokens.
5. **Packaging tests** (reused from ada-csharp `tests/packaging/verify-package.sh`, simplified):
   install the `.nupkg` into a NativeAOT console with
   `<TrimmerRootAssembly Include="TypeSafeSharp" />` and warnings as errors, a NativeAOT web app
   that calls `AddTypeSafe`, and a `net481` console at `<LangVersion>7.3</LangVersion>` with no
   `System.Net.Http` reference. Each runs one stubbed call with a plain Choice question.
6. **API drift check.** `live.yml` holds one nightly job, `openapi-drift`, with no secret. It
   downloads `https://api.typesafe.ai/openapi.json` and compares it with
   `tests/contract/openapi.snapshot.json`. On any difference the job fails and shows the diff in
   the step summary, and GitHub mails the owner. Updating the snapshot is a reviewed PR, which is
   where a breaking change gets its ADR or changelog entry.
7. **Docs parity check, manual.** The release runbook asks the releaser to read the JS and
   Python changelogs (`docs.typesafe.ai/sdk/*/changelog`) since the last release and record in
   the changelog entry which entries were ported, skipped, or not applicable.
8. **No sample or snippet jobs in CI.** README code is copied from
   `sample/` (in this repository, amended 2026-10-06), and after each release the runbook builds the sample
   against the nuget.org version and runs Quickstart once (ADR-0011).
9. **Culture test.** One test class runs the fixtures, the `Retry-After: 1.5` case and
   `ErrorMessage` under `tr-TR` and `de-DE`, setting both `CultureInfo.CurrentCulture` and
   `CurrentUICulture`.

## Consequences

The `netstandard2.0` asset is exercised on .NET Framework on every PR. Server-side schema
changes surface within a day.

One key lives in GitHub, scoped to an environment that only `main` can use, so a pull request,
including one from a fork, never sees it. A leak means revoking one CI key, not the
maintainer's. If the key is missing, the live tests fail rather than skip, so a silent skip
cannot pass for green.

GitHub disables scheduled workflows after 60 days without repository activity, which would stop
the drift check without a failure. The runbook asks the releaser to check that `live.yml` ran
this week (PLAN.md section 8, R10).

## Alternatives considered

**`RichardSzalay.MockHttp`.** Fine library. Rejected to keep the test graph the same shape as
ada-csharp, and because one fifty-line handler covers everything here.

**Live tests on every PR.** Rejected for secret exposure and cost.

**Live tests run by hand before each tag, no key in GitHub.** Rejected on 2026-09-25. Breaks
between releases went unseen, and a release step that depends on one person's machine is the
one that gets skipped. The API usage is small enough for a weekly run.

**Nightly live tests.** Rejected. The drift job already catches schema changes daily; weekly
live runs catch behaviour changes at a seventh of the cost.

**Generate the client from the OpenAPI document.** Rejected. The document is small and does not
capture the retry, header, and validation behaviour, which is most of the work. It is used as a
contract, not a source.
