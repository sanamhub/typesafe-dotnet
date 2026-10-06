# Contributing

Bug reports, fixes and documentation improvements are welcome. For a new feature or an API change,
open an issue first so the design can be agreed before you write code.

This project follows the [Code of Conduct](CODE_OF_CONDUCT.md). Report security problems as
described in [SECURITY.md](SECURITY.md), not in a public issue.

## Scope

TypeSafeSharp is an unofficial client for TypeSafe AI's System One API. Answers come from
TypeSafe's service. If the API returns a wrong answer or an error the documentation does not
describe, and the official [JavaScript](https://github.com/typesafe-ai/typesafe-sdk-js) or
[Python](https://github.com/typesafe-ai/typesafe-sdk-python) SDK gets the same response, the
problem is the service, not this package.

## Setup

You need the .NET SDK version in [`global.json`](global.json). On Windows the tests also run on
.NET Framework 4.8.1, which ships with Windows.

```bash
dotnet build -c Release
dotnet test -c Release -- --filter-not-trait "Category=Live"
```

On Linux or macOS add `-f net10.0` before the `--`.

The live tests call the real API and need a key in `TYPESAFE_API_KEY`. You do not need them to
contribute; CI runs them weekly. Never put a key in code, tests, issues or pull requests.

## Pull requests

- Keep a pull request to one change. Small is easier to review.
- Add or update tests for any behaviour change. Test data is made up; never use real customer text.
- Public API changes go in the project's `PublicAPI.Unshipped.txt`. The build fails if you forget.
- Add a line to the `Unreleased` section of [`CHANGELOG.md`](CHANGELOG.md) for anything a user of
  the package would notice.
- A significant design decision gets an ADR in [`docs/adr/`](docs/adr). ADRs are not edited after
  they are accepted; a later ADR supersedes an earlier one.
- CI must pass. It builds and tests on Windows, Linux and macOS.

## Commit messages

[Conventional Commits](https://www.conventionalcommits.org/): `type(scope): summary`, in the
imperative, lower case, no trailing period. Types: `feat`, `fix`, `docs`, `chore`, `refactor`,
`test`, `build`, `ci`, `perf`. The body says why, not what.

```
fix(retry): honour retry-after-ms before Retry-After

Retry-After in seconds alone rounds every wait up to a whole second.
```

## Code style

The rules in [`.editorconfig`](.editorconfig) are enforced by the build. Public members need XML
documentation that says what the member does, what it returns and what breaks it. The full house
style is in [`.claude/skills/writing-style/SKILL.md`](.claude/skills/writing-style/SKILL.md).
