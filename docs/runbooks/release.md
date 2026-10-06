# Release runbook

Adapted from `sanamhub/ada-csharp`. Nothing here runs yet. The workflows in
[PLAN.md section 6](../PLAN.md#6-ci-and-release) land in phases P0 (`ci.yml`), P3 (`live.yml`)
and P4 (`release.yml`).

Both packages, `TypeSafeSharp` and `TypeSafeSharp.Extensions.DependencyInjection`, always ship
together with the same version (ADR-0011).

## Before tagging

1. `CHANGELOG.md` has an entry for the version, moved out of Unreleased. Entries that break
   something start with `**Breaking:**`. The GitHub release notes are built from that section.
2. `Directory.Build.props` has the matching `<Version>`. It is the only place the version lives.
3. Read the `typesafe-sdk-js` and `typesafe-sdk-python` changelogs since the last release. Port
   or reject each behaviour change, record which entries were ported, skipped or not applicable
   in the changelog entry, and update the parity table in `README.md` (ADR-0012).
4. `live-tests` in `live.yml` is green within the last 24 hours. If not, run it:

   ```bash
   gh workflow run live.yml
   ```

   A red run needs a decision before release, not a rerun, unless the log shows a 429 or 5xx
   from TypeSafe. If the CI key leaks (a log, an issue, a commit), revoke it in the TypeSafe
   console first, then replace the `live` environment secret. Treat any commit that contained it
   as exposed even after a force push.
5. The last `openapi-drift` run in `live.yml` is green. A red drift job means the API changed and
   needs a decision before release, not a rerun.
6. `PublicAPI.Unshipped.txt` entries are moved to `PublicAPI.Shipped.txt` in both projects.
7. Pack locally (`dotnet pack -c Release -o artifacts/packages`), open each `.nupkg` as a zip,
   and check the dependency groups in its `.nuspec` match
   [PLAN.md section 4.2](../PLAN.md#42-dependencies) exactly (AC-4.2).
8. CI is green on `main`.

The `preflight` job checks items 1, 2 and 6 automatically (tag, `<Version>` and changelog
section agree, `PublicAPI.Unshipped.txt` empty), so a mistake there costs seconds rather than a
whole release.

## Releasing

```bash
git tag v0.1.0
```

```bash
git push origin v0.1.0
```

That triggers `release.yml`:

| Job | What it does |
| --- | --- |
| `preflight` | Checks the tag, `<Version>` and changelog section agree, and `PublicAPI.Unshipped.txt` is empty. |
| `verify` | Packs both packages once, checks both file names carry the tag version, consumes them from the NativeAOT console and web app, and uploads the tested files. |
| `verify-netfx` | Downloads those files on Windows and consumes them from the `net481` console at C# 7.3. |
| `publish` | Waits on the `production` environment approval, then downloads exactly the files `verify` tested, pushes the core package then the DI package, attests provenance and creates the GitHub release. |

Approve the deployment on the run's page, under `publish`, `Review deployments`.

The workflow creates the GitHub release, with notes from the changelog section and the `.nupkg`
and `.snupkg` files attached. A tag containing `-` is marked as a prerelease. Do not create a
release by hand; that only makes a duplicate.

`workflow_dispatch` is always a dry run: it stops after `verify` and `verify-netfx`, before the login, the push and
the release.

## Rolling back

**A published NuGet package cannot be deleted.** Unlisting is the only rollback, and there is no
long-lived API key to script it with. Unlist both packages, never one, or users get a core and a
DI package that do not match:

1. On nuget.org, open each package, Manage package, Listing, and clear "List in search results"
   for the bad version.
2. On the same page, deprecate the bad version with reason "Critical bugs" and the fixed version
   as the alternate, once it exists.
3. Ship a patch release with the revert.
4. For a security bug, publish a GitHub security advisory for the repository, so NuGetAudit warns
   everyone who restores the bad version.

An unlisted version stays resolvable for anyone who pinned it and disappears from search and
version ranges.

## After releasing

1. Read the release page. Confirm the notes match the changelog and all four package files are
   attached.
2. Build the sample against the published version and run Quickstart once, with your own key in
   `TYPESAFE_API_KEY`. This is the post-deploy verification (org standard section 12).
   nuget.org can take up to an hour to validate and index a new package, so a restore failure
   in the first hour usually means wait, not roll back.
3. Check both `live.yml` jobs ran this week. GitHub disables schedules after 60 days without
   repository activity; if they stopped, re-enable the workflow under Actions (PLAN.md section 8,
   R10).
4. Optional: `gh attestation verify <file>.nupkg --repo sanamhub/typesafe-dotnet` confirms a
   package came from this workflow.
5. Open the next `Unreleased` section in `CHANGELOG.md` and bump `<Version>`.

## One-time setup on nuget.org

Publishing uses trusted publishing, so no long-lived NuGet API key exists anywhere. nuget.org
checks a short-lived GitHub OIDC token against a policy and returns a key that lasts one hour.

Register the policy at nuget.org, under your username, Trusted Publishing:

| Field | Value |
| --- | --- |
| Repository Owner | `sanamhub` |
| Repository | `typesafe-dotnet` |
| Workflow File | `release.yml` (file name only) |
| Environment | `production` |
| Package scope | `TypeSafeSharp*` |

Scope the ada-csharp policy to `Ada.Url` the same way, so neither repository's workflow can
publish the other's packages. Then add one repository secret, `NUGET_USER`, holding the nuget.org
profile name, not the email address.

On GitHub, `production` deploys from `v*` tags only with the maintainer as required reviewer,
and a repository ruleset restricts who can create or delete `v*` tags.
