# Working in this repository

Instructions for anyone, human or AI agent, who changes code or docs here. `CLAUDE.md` imports
this file.

## What this is

TypeSafeSharp, an unofficial .NET client for TypeSafe AI's System One API. The design is
[docs/PLAN.md](docs/PLAN.md) and [docs/adr](docs/adr). The build order is
[docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md): take the lowest-numbered task that is not done,
finish it completely, and stop.

## Commands

```bash
dotnet build -c Release
```

```bash
dotnet test -c Release -- --filter-not-trait "Category=Live"
```

On Linux or macOS, where `net481` cannot run, add `-f net10.0` before the `--`.

```bash
dotnet pack -c Release -o artifacts/packages
```

A task is done only when all three pass with zero warnings, on your machine, before you open the
PR. Warnings are errors here; do not add `NoWarn` or `#pragma` to make one go away unless the
task tells you to, and then with a one-line reason.

## Hard rules

1. **Never put a real API key anywhere**: code, tests, fixtures, logs, issues, PR text, commit
   messages or prompts to another tool. Tests use `ts_test_0000000000000000`. The live suite reads
   `TYPESAFE_API_KEY` from the environment only; in CI that is the `live` environment secret,
   which the maintainer sets. Never create, set, print or ask for it.
2. **Never use real customer data.** Fixture and test text is made up.
3. **The public API is PLAN.md section 3, exactly.** Anything else is `internal`. If a task seems
   to need a public member that is not there, stop and ask; do not add it.
4. **Both targets, one behaviour.** The only allowed `#if` is `#if NET`, and only in
   `src/TypeSafeSharp/Http/HttpDefaults.cs`. The APIs that `netstandard2.0` lacks are listed in
   IMPLEMENTATION.md, section "netstandard2.0 traps".
5. **No new package references** beyond PLAN.md section 4.2 without an ADR.
6. **`ConfigureAwait(false)` on every await in `src`.** The analyzer enforces it.
7. **Document every exception.** Each public method lists every exception it can throw with
   `<exception cref="...">` in its XML docs, including `OperationCanceledException`.
8. **Follow the writing-style skill** (`.claude/skills/writing-style/SKILL.md`) for every doc,
   XML comment, code comment, commit and PR description.

## Commits and pull requests

- Conventional Commits, as in the writing-style skill. One task per PR, named after its task id,
  for example `feat(retry): T11 retry loop with budget`.
- No AI attribution trailers in commits or PRs (`Co-Authored-By`, `Generated with`). This is the
  repository's rule (PLAN.md Q7); the maintainer changes it here if that decision changes.
- The PR description follows the shape in the writing-style skill, then adds the task's "Done
  when" items with a tick for each and the final lines of the three commands above.
- A change a package user would notice adds a line under `Unreleased` in `CHANGELOG.md`.

## Exceptions to the org standard

Recorded in `CLAUDE.md`, with the approver.
