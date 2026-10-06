# ADR-0011: Versioning and release through NuGet trusted publishing

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06
- **Relates to:** org standard sections 9 and 12, and `sanamhub/ada-csharp` ADR practice

## Context

The org standard asks for SemVer with a changelog, green CI and an approval before a production
deploy, a rollback plan, and post-deploy verification. `sanamhub/ada-csharp` already meets this
with a pattern that has shipped three releases: version in the `.csproj`, a `v*` tag, a
preflight that checks tag, project and changelog agree, a `production` environment approval,
NuGet trusted publishing, and a separate job that installs the published package from
nuget.org.

The TypeSafe API moves fast. The official SDKs went from their first release, 0.5.7, to a
breaking 0.6.0 in four days (JS) and one day (Python), and Python shipped another breaking
change in 0.7.0 three days later.

## Decision

1. **Reuse the ada-csharp release pipeline** (`release.yml`, `scripts/changelog-section.sh`,
   `docs/runbooks/release.md`), minus everything native: no RID matrix, no checksums manifest,
   no native SBOM step, and no `verify-published.yml`. PLAN.md section 6 lists the diff.

   Where it differs from ada-csharp: the `verify` job packs both packages once, checks both file
   names carry the tag version, runs the packaging tests on those files, and uploads them. The
   `publish` job (ubuntu-24.04, environment `production`) downloads exactly those files, so the
   bytes tested are the bytes pushed; ada-csharp re-packs at publish, which this fixes. It pushes
   core then DI with `--skip-duplicate`, attests provenance with `actions/attest`, and creates
   the GitHub release.

   `publish` has `id-token: write`, `attestations: write`, and `contents: write` (for the
   release). `workflow_dispatch` is a dry run only: the whole `publish` job carries
   `if: github.event_name == 'push'`, because the `production` environment refuses deployments
   that do not come from a `v*` tag. The workflow uses `concurrency: release-${{ github.ref }}`
   with no cancel.

   Post-deploy verification (org standard section 12) is a runbook step, not a workflow: after
   each release the maintainer builds the sample against the nuget.org version and runs
   Quickstart once with their own key.
2. **Version is set by hand in `Directory.Build.props`** (shared by both packages), not by MinVer.
   The `preflight` job reads `<Version>` from that file and fails unless the tag, the property
   and the `CHANGELOG.md` section agree and `PublicAPI.Unshipped.txt` is empty.
3. **SemVer, first published as `0.1.0-alpha.1` at P4**,
   then `0.1.0`. While `0.x`, a breaking change bumps the minor. `1.0.0` ships when the exit
   criteria in PLAN.md section 7 are met.

   `EnablePackageValidation` is on from P0, since its cross-target surface check needs no
   baseline. The baseline (`PackageValidationBaselineVersion`) is deferred to 1.0 and is part of
   the exit criteria in PLAN.md section 7.1. Until then `PublicAPI.Shipped.txt` catches
   accidental changes.
4. **Our version numbers are independent of the official SDKs.** The README carries a parity
   table ("0.1.0 matches API 0.2.0, JS 0.6.0, Python 0.7.1 behaviour") so nobody assumes `0.6.0`
   here means JS `0.6.0`.
5. **Trusted publishing only.** `NuGet/login` exchanges the workflow's OIDC token for a one-hour
   key. No long-lived NuGet API key exists. The nuget.org policy is scoped to repository,
   `release.yml`, and environment `production`, and to the package glob `TypeSafeSharp*` if the
   policy form offers scopes (it did at review time; the owner confirms in the UI). ada-csharp's
   policy gets the same scoping to `Ada.Url`.

   The `production` environment deploys from `v*` tags only and needs a required reviewer. A
   ruleset restricts who can create or delete `v*` tags.
6. **Rollback is unlist, deprecate, then patch.** nuget.org does not delete versions. The
   runbook's rollback section unlists the bad version of both package IDs, never one, in the
   nuget.org UI (Manage package, Listing), deprecates it with the fixed version as the
   alternate, and ships `x.y.(z+1)` with the revert. For a security bug the maintainer also publishes a GitHub
   security advisory (GHSA), so NuGetAudit warns consumers.

   Consumers pinned to the bad version keep it until they upgrade, which is stated in the release
   notes. The runbook has no `dotnet nuget delete` commands, since no long-lived key exists to
   run them with.
7. **Both packages release together** with the same version, from one tag.
8. **`CentralPackageTransitivePinningEnabled` is off,** so each nuspec lists exactly the reviewed
   package references. Before tagging, the releaser opens the packed `.nupkg` files and checks
   the dependency groups match PLAN.md section 4.2 (AC-4.2). That is a runbook step, not a
   script.

## Consequences

One release process across the maintainer's packages. A breaking API change from TypeSafe
becomes a minor bump with a changelog entry under `### Changed` that starts with
`**Breaking:**` and names the TypeSafe change and date.

Hand-set versions need an "open the next cycle" PR after each release. That is the ada-csharp
practice and it keeps the version visible in review.

## Alternatives considered

**MinVer (tag-derived versions).** Used by `TypeSafe.AI.Sdk`. Rejected for consistency with
ada-csharp, and because the preflight check is simpler when the version is a literal.

**A long-lived `NUGET_API_KEY` secret.** Rejected. It is the credential most likely to leak and
has to be rotated by hand.

**Versions that mirror the official SDKs.** Rejected. We will ship fixes they do not need, and
they will ship changes we do not need.
