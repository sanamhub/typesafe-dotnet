# Security policy

## Supported versions

Only the latest release on [nuget.org](https://www.nuget.org/packages/TypeSafeSharp) receives
fixes.

## Reporting a vulnerability

Report privately through
[GitHub security advisories](https://github.com/sanamhub/typesafe-dotnet/security/advisories/new).
Do not open a public issue.

Include the package version, the target framework, and the smallest code that shows it. Do not
include your API key or real request text. Expect a first reply within a week.

## Not for this repository

- **A leaked API key.** Revoke it at once at `https://console.typesafe.ai/keys` and create a new
  one. This project cannot revoke keys.
- **A vulnerability in TypeSafe's service** (`api.typesafe.ai`, the console, the models). This is an
  unofficial client with no access to the service; report those to TypeSafe through the contacts
  at `https://docs.typesafe.ai`.
