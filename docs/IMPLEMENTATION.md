# TypeSafeSharp implementation tasks

The build order for TypeSafeSharp, written so that an implementer who has not read the design
discussion can follow it one task at a time. [PLAN.md](PLAN.md) says what to build and the ADRs
say why. This file says in what order, with which files, and how to know a task is done.

The reference code in [Appendix A](#appendix-a-reference-code) was compiled for
`netstandard2.0` and `net10.0` with the analyzer settings from task T01 (`AnalysisLevel
latest-all`, warnings as errors, AOT analysis on `net10.0`), then run on `net10.0` and `net481`.
Use it as given unless a task says otherwise. The few blocks edited after that check say so.

## How to use this file

1. Read [AGENTS.md](../AGENTS.md) once. Its hard rules apply to every task.
2. Take the lowest-numbered task whose dependencies in the [task index](#task-index) are all
   done. Tasks marked **HUMAN** need the maintainer (accounts, legal, approvals, the real API
   key); an agent skips them and says so.
3. Read the documents listed under "Read first", then do the steps in order.
4. Write the tests listed under "Tests" in the same PR as the code. Then raise
   `--minimum-expected-tests` in each test csproj you added tests to (T02 step 3) to a little
   below the new count.
5. AGENTS.md hard rule 7 (`<exception cref>` for every exception a public method can throw)
   applies to every public method you add. The tasks' "Done when" lists call this the "XML
   docs item".
6. Check every "Done when" item. Run the three commands from AGENTS.md. Open one PR per task.
7. If a step is impossible or contradicts PLAN.md section 3, stop and report. Do not improvise
   public API, new dependencies or new `#if` blocks.

Each task has the same parts: **Goal**, **Read first**, **Files**, **Steps**,
**Tests**, **Done when**, and where useful **Traps**.

## Task index

| ID | Title | Phase | Depends on |
| --- | --- | --- | --- |
| [T00](#t00-human-prerequisites) | HUMAN: prerequisites | P0 | none |
| [T01](#t01-repository-skeleton) | Repository skeleton | P0 | T00 |
| [T02](#t02-ci-workflow) | CI workflow | P0 | T01 |
| [T03](#t03-exceptions-and-guard) | Exceptions and `Guard` | P1 | T01 |
| [T04](#t04-questions) | Questions | P1 | T03 |
| [T05](#t05-request-and-request-writer) | Request and request writer | P1 | T04 |
| [T06](#t06-answers-response-and-response-reader) | Answers, response and response reader | P1 | T04 |
| [T07](#t07-options-api-key-and-settings-snapshot) | Options, API key and settings snapshot | P1 | T03 |
| [T08](#t08-error-mapping-and-retry-after) | Error mapping and `Retry-After` | P1 | T03, T05, T06 |
| [T09](#t09-client-transport-and-models) | Client transport and `/v1/models` | P1 | T05, T06, T07, T08 |
| [T10](#t10-packaging-consumers) | Packaging consumers | P1 | T02, T09 |
| [T11](#t11-retry-loop-timeouts-and-budget) | Retry loop, timeouts and budget | P2 | T09 |
| [T12](#t12-logging-and-tracing) | Logging and tracing | P2 | T11 |
| [T13](#t13-batch-helper) | Batch helper | P3 | T11 |
| [T14](#t14-dependency-injection-package) | Dependency injection package | P3 | T10, T11 |
| [T15](#t15-readmes) | READMEs | P3 | T13, T14 |
| [T16](#t16-api-drift-check-and-scheduled-live-tests) | API drift check and scheduled live tests | P3 | T11 |
| [T17](#t17-release-pipeline) | Release pipeline | P4 | T10, T12, T15, T16 |
| [T18](#t18-human-first-prerelease-then-010) | HUMAN: first prerelease, then 0.1.0 | P4 | T17 |

## netstandard2.0 traps

The library compiles for `netstandard2.0` and `net10.0` from the same source. These APIs exist
on `net10.0` only, and the compiler error on the other target is easy to misread. Use the right
column. Several analyzer rules suggest the left column on `net10.0`; T01 turns those rules off
for `src`.

| Do not use | Use | Why |
| --- | --- | --- |
| `ArgumentNullException.ThrowIfNull(x)` | `Guard.NotNull(x)` ([A.1](#a1-guard)) | not on ns2.0 |
| `Random.Shared` | `Jitter.Next()` ([A.5](#a5-jitter)) | not on ns2.0; `new Random()` is not thread-safe |
| `Task.Delay(delay, timeProvider, ct)`, `new CancellationTokenSource(delay, timeProvider)` | `Timing.DelayAsync`, `Timing.CancelAfter` ([A.4](#a4-timing)) | not on ns2.0, and T11 tests need `TimeProvider` |
| `cts.CancelAsync()` | `cts.Cancel()` | not on ns2.0 |
| `s.Contains("x", StringComparison.OrdinalIgnoreCase)` | `s.IndexOf("x", StringComparison.OrdinalIgnoreCase) >= 0` | not on ns2.0 |
| `s.Contains('x')`, `s.Split('x')` with options, `StartsWith('x')` | string overloads | not on ns2.0 |
| `dict.GetValueOrDefault(k)`, `dict.TryAdd(k, v)` | `TryGetValue`, `ContainsKey` then `Add` | not on ns2.0 |
| `new Dictionary<K, V>(IReadOnlyDictionary<K, V>)`, the `IEnumerable<KeyValuePair<K, V>>` constructor | `new Dictionary<K, V>(source.Count, comparer)` then a `foreach` with `Add` | neither constructor exists on ns2.0 |
| `foreach (var (key, value) in dict)` | `foreach (var pair in dict)` then `pair.Key`, `pair.Value` | `KeyValuePair.Deconstruct` not on ns2.0 |
| `OperatingSystem.IsWindows()`, `OperatingSystem.IsBrowser()` | `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`, `OSPlatform.Create("BROWSER")` | `OperatingSystem.Is*` not on ns2.0 |
| `content.ReadAsStringAsync(ct)`, `ReadAsByteArrayAsync(ct)` | `ReadAsByteArrayAsync()` after `SendAsync` with the default `ResponseContentRead` | no token overloads on ns2.0; the body is already buffered under the attempt token |
| `HttpClient.Send(...)` (sync) | nothing | ADR-0007: async only |
| `SocketsHttpHandler` | `HttpDefaults.CreateHandler()` ([A.11](#a11-httpdefaults)), the only `#if NET` | not on ns2.0 |
| `DateOnly`, `TimeOnly`, `Half`, `Index`/`Range` on arrays | `string`, `double`, loops | not on ns2.0 (PolySharp adds syntax, not these types) |
| `init` setters, `record` types in the public API | `{ get; set; }`, sealed classes | C# 7.3 callers cannot use them (PLAN 3) |
| `JsonSerializer.Serialize(x)` without type info | `Utf8JsonWriter` ([A.9](#a9-requestwriter)) or `JsonTypeInfo<T>` | reflection, fails AOT analysis |
| adding a caller's `JsonNode` to a new `JsonObject` | `node.WriteTo(writer)` ([A.9](#a9-requestwriter)) | a node can have one parent; questions are shared across calls |

## T00 HUMAN: prerequisites

**Goal.** The decisions an agent cannot make.

**Steps.**
1. Read PLAN.md and ADR-0001 to ADR-0013. Change the status of each accepted ADR to
   `accepted` and fill in "Approved by". Update the exceptions table in `CLAUDE.md` the same way,
   including the single-maintainer review exception.
2. Answer Q1 (name) in PLAN.md section 9. If the name changes, change it everywhere before
   T01; after the first publish it cannot change (ADR-0002). Q3 and Q4 are closed.
3. Create the public GitHub repository `sanamhub/typesafe-dotnet`, turn on the security
   settings in PLAN.md section 6.3 step 1, and push the planning files (`README.md`,
   `AGENTS.md`, `CLAUDE.md`, `docs/`, `.claude/`).
4. The sample lives in `sample/` in the same repository (decided 2026-10-06). CI never builds it:
   it restores the published package, which does not exist until P4 (ADR-0012).
5. Create a dedicated TypeSafe API key for CI, separate from your own. It goes into the `live`
   environment in T16. Optional: email TypeSafe about the use case and ask for a higher limit
   on that key (PLAN.md section 6.3, step 6). Nothing waits on the reply.

**Done when.** ADRs accepted or amended, Q1 answered, the repository exists.

## T01 Repository skeleton

**Goal.** An empty solution that builds and tests green on both targets with the final build
settings, so every later task inherits them.

**Read first.** PLAN.md sections 4.1 to 4.3; ADR-0001, ADR-0003, ADR-0013; the ada-csharp
repository (`https://github.com/sanamhub/ada-csharp`) for the files to copy.

**Files.**
```
TypeSafeSharp.slnx
global.json  NuGet.Config  .editorconfig  .gitattributes  .gitignore
Directory.Build.props  Directory.Packages.props
LICENSE  THIRD-PARTY-NOTICES.txt  CHANGELOG.md  CONTRIBUTING.md  SECURITY.md  CODE_OF_CONDUCT.md
TypeSafeSharp.snk
.github/dependabot.yml  .github/pull_request_template.md  .github/ISSUE_TEMPLATE/*
src/TypeSafeSharp/TypeSafeSharp.csproj  PACKAGE.md  PublicAPI.Shipped.txt  PublicAPI.Unshipped.txt
src/TypeSafeSharp/AssemblyInfo.cs  TypeSafeClientOptions.cs
src/TypeSafeSharp.Extensions.DependencyInjection/(csproj, PACKAGE.md, the two PublicAPI files)
tests/TypeSafeSharp.Tests/TypeSafeSharp.Tests.csproj  SmokeTests.cs
tests/TypeSafeSharp.Extensions.DependencyInjection.Tests/(same)
```

**Steps.**
1. Copy unchanged from ada-csharp: `.gitattributes`, `NuGet.Config`, `global.json`,
   `CODE_OF_CONDUCT.md`, `LICENSE` (change the copyright holder line to
   `TypeSafeSharp contributors`).
2. Write `.editorconfig`: start from ada-csharp's, delete the Ada-specific rules (`CA1401`,
   `SYSLIB1054`, the `Interop/*.cs` section, `CA1028`, `CA5393`, `CA2000 = none`, `CA1034`, the
   benchmarks sections), then append the block in
   [Appendix A.0](#a0-editorconfig-block-for-src). Keep the test section that turns off
   `CS1591`, `CA1707` and `CA1515`, and add `dotnet_diagnostic.CA2007.severity = none` there
   (xUnit wants tests without `ConfigureAwait`).
3. Write `Directory.Build.props` from ada-csharp's with the changes in PLAN.md section 4.3:
   delete `TargetFramework`, `InvariantGlobalization`, `AdaUrlUpstreamTag`; set `Product`,
   `Company`, `Copyright`, `RepositoryUrl`, `Version` `0.1.0`, `AssemblyVersion` `0.0.0.0`,
   `SignAssembly`, `AssemblyOriginatorKeyFile`, and `WarningsNotAsErrors` `NU1901;NU1902`.
4. Write `Directory.Packages.props` with `ManagePackageVersionsCentrally` set to `true` and
   `CentralPackageTransitivePinningEnabled` set to `false` (ada-csharp turns it on; do not copy
   that). With pinning off, the nuspec lists exactly the direct references PLAN.md section 4.2
   reviewed. Add one `PackageVersion` per row of PLAN.md section 4.2 (skip
   `Microsoft.SourceLink.GitHub`; the SDK includes SourceLink). Every version is exact; central
   package management rejects floating versions such as `10.0.x` (NU1011). Note
   `Microsoft.Extensions.TimeProvider.Testing` is `10.10.0`: it ships from dotnet/extensions,
   which versions as `10.<minor>.0`, so `10.0.12` does not exist and restore fails with NU1102.
5. Generate the strong-name key once. The maintainer runs this, commits `TypeSafeSharp.snk`, and
   never regenerates it (ADR-0013). Save as `make-key.cs` outside the repository and run
   `dotnet run make-key.cs` (a .NET 10 file-based app; checked on Windows, it writes a
   1,172-byte key):

   ```csharp
   using System.Security.Cryptography;

   using var rsa = new RSACryptoServiceProvider(2048);
   File.WriteAllBytes("TypeSafeSharp.snk", rsa.ExportCspBlob(includePrivateParameters: true));
   ```

6. Create `src/TypeSafeSharp/TypeSafeSharp.csproj` exactly as PLAN.md section 4.3 shows. Keep
   `PolySharpIncludeRuntimeSupportedAttributes`: T09's `[assembly: UnsupportedOSPlatform]` needs
   it on `netstandard2.0`. Add these item groups:

   ```xml
   <ItemGroup>
     <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
     <PackageReference Include="PolySharp" PrivateAssets="all" />
     <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" PrivateAssets="all" />
     <AdditionalFiles Include="PublicAPI.Shipped.txt;PublicAPI.Unshipped.txt" />
   </ItemGroup>
   <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
     <PackageReference Include="System.Text.Json" />
     <PackageReference Include="System.Diagnostics.DiagnosticSource" />
     <PackageReference Include="Microsoft.Bcl.TimeProvider" />
     <PackageReference Include="Microsoft.Bcl.AsyncInterfaces" />
   </ItemGroup>
   ```

   `PACKAGE.md` gets one line for now ("Unofficial .NET client for TypeSafe AI. Full README:
   https://github.com/sanamhub/typesafe-dotnet"); T15 writes the real one.
7. `src/TypeSafeSharp/AssemblyInfo.cs` grants tests access to internals, which needs the full
   public key. Build the core project once (it is signed by now), then save this as `pubkey.cs`
   outside the repository and run
   `dotnet run pubkey.cs -- src/TypeSafeSharp/bin/Release/net10.0/TypeSafeSharp.dll`:

   ```csharp
   using System.Reflection;

   var key = AssemblyName.GetAssemblyName(args[0]).GetPublicKey()!;
   Console.WriteLine(Convert.ToHexString(key).ToLowerInvariant());
   ```

   It prints 576 hex characters. Paste them into:

   ```csharp
   using System.Runtime.CompilerServices;

   [assembly: InternalsVisibleTo("TypeSafeSharp.Tests, PublicKey=<paste>")]
   [assembly: InternalsVisibleTo("TypeSafeSharp.Extensions.DependencyInjection, PublicKey=<paste>")]
   ```

8. Create the DI project the same way: `TargetFrameworks netstandard2.0;net10.0`, the same
   `IsAotCompatible` condition, `EnableConfigurationBindingGenerator true`, `PackageId`
   `TypeSafeSharp.Extensions.DependencyInjection`, its own `Description` and `PACKAGE.md`, a
   `ProjectReference` to the core, and package references to `Microsoft.Extensions.Http` and
   `Microsoft.Extensions.Options.ConfigurationExtensions`.
9. Create both test projects: `TargetFrameworks net10.0;net481`, `OutputType Exe`,
   `IsPackable false`, references to `xunit.v3`, `Microsoft.Testing.Extensions.CodeCoverage`,
   `Microsoft.Testing.Extensions.TrxReport`, `Microsoft.Extensions.TimeProvider.Testing`, and on
   `net481` only `Microsoft.NETFramework.ReferenceAssemblies` (private) plus
   `<Reference Include="System.Net.Http" />`. Test projects are signed too (the props do that),
   which `InternalsVisibleTo` requires.
10. The smoke test needs one public type. Create `src/TypeSafeSharp/TypeSafeClientOptions.cs` as
    an empty `public sealed class TypeSafeClientOptions` with an XML summary (T07 fills it in),
    and list it in `PublicAPI.Unshipped.txt` (the analyzer's code fix writes both lines, the type
    and its constructor). Then one smoke test per test project, so the platform does not exit
    with "zero tests ran":

    ```csharp
    public sealed class SmokeTests
    {
        [Fact]
        public void Assembly_IsStrongNamed()
            => Assert.NotEmpty(typeof(TypeSafeSharp.TypeSafeClientOptions).Assembly.GetName().GetPublicKeyToken()!);
    }
    ```

11. `TypeSafeSharp.slnx` lists the four projects. Adapt `CONTRIBUTING.md`, `SECURITY.md`, the
    issue templates, the PR template and `dependabot.yml` as PLAN.md section 4.1 says.
    `THIRD-PARTY-NOTICES.txt` credits `typesafe-sdk-js` ("Copyright (c) 2026 TypeSafe", MIT) and
    `typesafe-sdk` for Python ("MIT, per pyproject.toml"; its LICENSE file has a placeholder
    copyright line).
12. `CHANGELOG.md` in Keep a Changelog format with an `## [Unreleased]` section.

**Tests.** The two smoke tests.

**Done when.**
- [ ] `dotnet build -c Release` succeeds with zero warnings for all four projects and both
      targets.
- [ ] `dotnet test -c Release` runs the smoke tests on `net10.0` and `net481` on Windows. On
      Linux or macOS run `dotnet test -c Release -f net10.0` (`-f` goes before any `--`).
- [ ] `dotnet pack -c Release -o artifacts/packages` produces two `.nupkg` and two `.snupkg`.
- [ ] `grep -rn "#if" src` prints nothing.

**Traps.** Microsoft.Testing.Platform exits with code 8 when no test ran; the smoke test avoids
that. Do not copy ada-csharp's `CA2000 = none` or its transitive pinning.

Found while building T01 (2026-10-06):
- xunit.v3 adds no global `using Xunit`; the test projects carry `<Using Include="Xunit" />`.
- `Microsoft.Extensions.TimeProvider.Testing` 10.10.0 warns that `net481` is untested, which
  breaks the zero-warning gate. The test projects set `SuppressTfmSupportBuildWarnings`, with the
  reason in a comment. Never set it in `src`.
- `ImplicitUsings` is off in `src`, so every file lists its usings, as the appendix code does.
- `.gitattributes` adds `*.snk binary`, or `eol=lf` could corrupt the key on checkout.
- On a machine short of memory, MSBuild can fail with `OutOfMemoryException` or `Could not load
  ICU data`. Run `dotnet build-server shutdown` and build with `-m:1`.

## T02 CI workflow

**Goal.** Every PR builds and tests on both operating systems, so later tasks are checked
automatically.

**Read first.** PLAN.md section 6.1; ada-csharp `.github/workflows/ci.yml`.

**Files.** `.github/workflows/ci.yml`, both test csproj files.

**Steps.**
1. Start from ada-csharp's `ci.yml`. Remove natives, arm, musl, macOS and the System.Uri report.
2. Jobs:
   - `build-test` with a matrix of `ubuntu-24.04` and `windows-2022`. Both run
     `dotnet build -c Release`. Windows runs
     `dotnet test -c Release --no-build -- --filter-not-trait "Category=Live" --report-trx`,
     which covers `net10.0` and `net481`. Linux runs
     `dotnet test -c Release --no-build -f net10.0 -- --filter-not-trait "Category=Live" --report-trx --coverage --coverage-output-format cobertura`.
     Build options such as `-f` always come before `--`; everything after `--` goes to every
     test project.
   - Coverage summary, a step on the Linux leg only (`if: runner.os == 'Linux'`). It is shown,
     not enforced: 80% is a target (org rule 7).

     ```yaml
     - name: Coverage summary
       if: runner.os == 'Linux'
       run: |
         python3 - <<'EOF' >> "$GITHUB_STEP_SUMMARY"
         import glob, xml.etree.ElementTree as ET
         print("| Report | Line coverage |\n| --- | --- |")
         for path in sorted(glob.glob("tests/**/TestResults/**/*.cobertura.xml", recursive=True)):
             rate = float(ET.parse(path).getroot().get("line-rate"))
             print(f"| {path} | {rate:.1%} |")
         EOF
     ```

   - `no-if`: fails when `grep -rn "^\s*#if" src | grep -v "#if NET$"` prints anything, or when
     any `#if NET` is outside `src/TypeSafeSharp/Http/HttpDefaults.cs`.
3. In each test csproj, set a minimum test count so a leg that discovers nothing fails. Put it
   in the csproj, not on the command line: arguments after `--` go to every project, so one
   number cannot fit both.
   `<TestingPlatformCommandLineArguments>--minimum-expected-tests 1</TestingPlatformCommandLineArguments>`.
   Later tasks raise the number (see "How to use this file", step 4).
4. `permissions: contents: read` at the top. `persist-credentials: false` on every checkout. Pin
   every action to a full commit SHA with a version comment, copying the SHAs ada-csharp uses.
5. Set `DOTNET_NOLOGO`, `DOTNET_CLI_TELEMETRY_OPTOUT` and `CI=true` as ada-csharp does.

**Done when.**
- [ ] A PR with the T01 skeleton shows three green checks (two `build-test` legs and `no-if`),
      and the Linux leg's job summary shows a coverage table.
- [ ] A test PR that adds `#if DEBUG` to a file under `src` fails `no-if` (then close it).

6. Job `deps`, copied from ada-csharp `ci.yml`: `if: github.event_name == 'pull_request'`,
   permissions `contents: read` and `pull-requests: write`, `actions/dependency-review-action`
   with `fail-on-severity: high` and `comment-summary-in-pr: on-failure`. Keep ada's comment
   that it needs the Dependency graph enabled (T00 does that).

**Traps.** CodeQL runs through GitHub's default setup, not a workflow job. Do not add
`continue-on-error` to `deps`; ada-csharp removed it once the Dependency graph was on (its #58).

Found while building T02 (2026-10-06): with `global.json` selecting Microsoft.Testing.Platform,
`dotnet test` takes the platform options directly, with or without `--`; `ci.yml` passes them
directly, as ada-csharp does. `--results-directory TestResults` puts coverage files in the root
`TestResults/`, so the summary globs `TestResults/*.cobertura.xml`, not `tests/**`. The table
stays empty until T03 adds code with lines to cover.

## T03 Exceptions and Guard

**Goal.** Every exception type from ADR-0006, and the argument guard every later task uses.

**Read first.** ADR-0006; PLAN.md section 3.5.

**Files.** `src/TypeSafeSharp/Errors/*.cs` (one file per exception type plus
`ValidationError.cs` and `ApiErrorDetails.cs`), `src/TypeSafeSharp/Guard.cs`,
`src/TypeSafeSharp/TypeSafeModelFactory.cs` (exception part only).

**Steps.**
1. Copy `Guard` from [Appendix A.1](#a1-guard).
2. Create the tree from ADR-0006. Every exception type needs the three standard public
   constructors, `()`, `(string message)` and `(string message, Exception innerException)`,
   because CA1032 requires them. The SDK itself uses `internal` constructors that set the extra
   properties. Pattern:

   ```csharp
   /// <summary>The server returned HTTP 429 after retries were used up.</summary>
   public sealed class TypeSafeRateLimitException : TypeSafeApiException
   {
       /// <summary>Creates an exception with a default message.</summary>
       public TypeSafeRateLimitException() { }

       /// <summary>Creates an exception with a message.</summary>
       public TypeSafeRateLimitException(string message) : base(message) { }

       /// <summary>Creates an exception with a message and an inner exception.</summary>
       public TypeSafeRateLimitException(string message, Exception innerException) : base(message, innerException) { }

       internal TypeSafeRateLimitException(ApiErrorDetails details, TimeSpan? retryAfter)
           : base(details) => RetryAfter = retryAfter;

       /// <summary>How long the server asked to wait, or null when it did not say.</summary>
       public TimeSpan? RetryAfter { get; }
   }
   ```

   `ApiErrorDetails` is an internal sealed class holding `Message`, `StatusCode`, `Endpoint`,
   `RequestId`, `Body` and `Headers`, so the internal constructors stay short.
3. `TypeSafeApiException` properties: `HttpStatusCode StatusCode`, `string Endpoint`
   (`"POST /v1/systemone"`), `string? RequestId`, `string? Body`,
   `IReadOnlyDictionary<string, IReadOnlyList<string>> Headers` (response headers; an empty
   dictionary when unset). The XML doc of `Body` says: "The raw response body. It can contain
   parts of the request, which may be customer data, so do not log it."
4. `TypeSafeUnprocessableEntityException.ValidationErrors` is `IReadOnlyList<ValidationError>`.
   `ValidationError` is a sealed class with get-only `IReadOnlyList<string> Location`,
   `string Message`, `string Type` and an internal constructor.
5. `TypeSafeTimeoutException : TypeSafeConnectionException` with `TimeSpan Timeout`.
   `TypeSafeResponseValidationException` with `string? JsonPath`; its internal constructor takes
   `(string jsonPath, string problem)` and builds the message
   `"The response did not match the API contract at {jsonPath}: {problem}."`.
6. `TypeSafeModelFactory.ApiException(int statusCode, string? message = null, string? requestId =
   null, TimeSpan? retryAfter = null)` returns the subclass for the status: 400, 401, 403, 404,
   422, 429, 529 (overloaded), other 5xx (internal server), anything else the base type. It sets
   `Endpoint` to `"POST /v1/systemone"`.
7. Add every public member to `PublicAPI.Unshipped.txt` (use the analyzer's code fix).

**Tests.** `ExceptionTests`: the factory returns the right type for each status in step 6;
`RetryAfter` and `RequestId` round trip; `Guard.NotNull(null)` throws `ArgumentNullException`
whose `ParamName` is the argument expression; `Guard.Positive` rejects `TimeSpan.Zero`,
`Timeout.InfiniteTimeSpan` and `TimeSpan.MaxValue`.

**Done when.**
- [ ] All exception types from ADR-0006 exist with XML docs; build is warning free.
- [ ] No exception type is `[Serializable]` (ADR-0006 rule 5).
- [ ] XML docs item (How to use, step 5) for every public constructor and
      `TypeSafeModelFactory.ApiException`.

## T04 Questions

**Goal.** `Question`, its three subclasses, the factory methods with validation, and the raw
`Question.FromJson` escape hatch.

**Read first.** PLAN.md section 3.2; ADR-0004; `docs.typesafe.ai/api.md` (question types).

**Files.** `src/TypeSafeSharp/Questions/Question.cs`, `NoulQuestion.cs`, `ChoiceQuestion.cs`,
`ScoreQuestion.cs`, `RawQuestion.cs`.

**Steps.**
1. `Question` is abstract with a `private protected` constructor, so only this assembly can
   derive from it. `Type` returns `"noul"`, `"choice"` or `"score"`.
2. Every factory method deep clones each `JsonNode` it receives (`node?.DeepClone()`) and stores
   the clone. Never store the caller's instance. Copy dictionaries with a loop (see
   [netstandard2.0 traps](#netstandard20-traps)):

   ```csharp
   var copy = new Dictionary<string, JsonNode?>(source.Count, StringComparer.Ordinal);
   foreach (var pair in source)
   {
       copy.Add(pair.Key, pair.Value?.DeepClone());
   }

   Options = new ReadOnlyDictionary<string, JsonNode?>(copy);
   ```

3. Validation, all `ArgumentException` with `paramName` set:
   - `Score`: `levels` null throws `ArgumentNullException`; fewer than 2 levels; any null level.
   - `Choice(instructions, params string[] options)`: null or empty array; a null or empty
     option name; a duplicate option name. Descriptions are all null.
   - `Choice(instructions, IReadOnlyDictionary<string, JsonNode?>)`: null or empty map; a null or
     empty key.
4. `Question.FromJson(JsonObject question)`: null throws `ArgumentNullException`. Deep clone the
   object. The clone's `type` must be a JSON string that is not empty, else `ArgumentException`
   with `paramName` `question`. Return an `internal sealed class RawQuestion : Question` that
   stores the clone as `internal JsonObject Json`, returns that string from `Type`, and passes
   `Json["instructions"]?.DeepClone()` to the base constructor as `Instructions`. Do not reject
   known types such as `"noul"`; the object goes to the server as given.
5. XML docs on every public member, with `<exception cref>` for each rule above. The XML doc of
   `FromJson` says it is for question types this SDK does not model yet, and that the object is
   sent as is.

**Tests.** `QuestionTests`:
- Each factory sets `Type` and the stored values.
- Each validation rule throws the right exception type and `ParamName`.
- A caller's node is cloned: mutating it after the factory call does not change the question,
  and the caller's node still has `Parent == null`.
- `FromJson` with `{"type": "ranking", "instructions": "Rank these", "criteria": ["a", "b"]}`
  gives `Type == "ranking"`; mutating the caller's object afterwards changes nothing; a missing,
  empty or non-string `type` throws.

**Done when.**
- [ ] Every question in the API reference can be built.
- [ ] `net10.0` build shows no IL warnings.
- [ ] XML docs item (How to use, step 5).

## T05 Request and request writer

**Goal.** `SystemOneRequest` and the internal writer that produces the request JSON.

**Read first.** PLAN.md section 3.2; ADR-0004; Appendix A.9; Appendix B request fixtures.

**Files.** `src/TypeSafeSharp/Models/SystemOneRequest.cs`,
`src/TypeSafeSharp/Serialization/RequestWriter.cs`,
`tests/TypeSafeSharp.Tests/Fixtures/requests/*.json`.

**Steps.**
1. `SystemOneRequest(JsonNode state, IReadOnlyDictionary<string, Question> questions)`: guard
   both, reject an empty map, a null or empty id, a null question. Copy the map into a read-only
   dictionary with `StringComparer.Ordinal`. `State` is stored as given (not cloned; the XML doc
   says not to mutate it during a call). `Model` and `ExtraBody` are `{ get; set; }`.
2. `RequestWriter.Write(SystemOneRequest request, string model)` returns `byte[]`. Base it on
   [Appendix A.9](#a9-requestwriter). Property order: `state`, `model`, `questions`, then
   `ExtraBody` entries. Each question writes `type`, `instructions` (null when absent), and
   `criteria` when the question has any:
   - Noul: `{"true": ..., "false": ...}` only when at least one of `WhenTrue`/`WhenFalse` is set;
     write both keys, null for the missing one.
   - Choice: an object of option name to description (null allowed).
   - Score: an array of levels.
   - `RawQuestion`: `question.Json.WriteTo(writer)` as the whole question value. Never add it to
     a parent.
3. Before writing, if `ExtraBody` has a key `state`, `model` or `questions`, throw
   `ArgumentException` naming the key.
4. Add the request fixtures from [Appendix B](#appendix-b-fixtures).

**Tests.** `RequestWriterTests`:
- Each fixture: build the same request in code, write it, and compare canonically with the
  fixture (parse both with `JsonNode.Parse` and compare with `JsonNode.DeepEquals`).
- `ExtraBody` fields appear; a colliding key throws.
- A `FromJson` question round trips: the written question equals the input object, and after
  writing, both the caller's object and `RawQuestion.Json` have `Parent == null`.
- AC-3.11: one `Question` used by 8 parallel `Write` calls gives identical bytes, and afterwards
  its nodes have `Parent == null` and `ExtraBody` is unchanged.

**Done when.**
- [ ] AC-3.2 and AC-3.11 pass.
- [ ] XML docs item (How to use, step 5) for `SystemOneRequest`.

**Traps.** `JsonNode.DeepEquals` exists in System.Text.Json 8 and later, so it works on both
test targets.

## T06 Answers, response and response reader

**Goal.** The answer types, `SystemOneResponse` with its typed accessors, and the reader that
turns bytes into them, including bodies that are not JSON at all.

**Read first.** PLAN.md section 3.3; ADR-0004; Appendix A.10; Appendix B response fixtures.

**Files.** `src/TypeSafeSharp/Answers/*.cs`, `src/TypeSafeSharp/Models/SystemOneResponse.cs`,
`Usage.cs`, `ModelCard.cs`, `src/TypeSafeSharp/Serialization/ResponseReader.cs`,
`src/TypeSafeSharp/TypeSafeModelFactory.cs` (answer part),
`tests/TypeSafeSharp.Tests/Fixtures/responses/*`.

**Steps.**
1. Answer classes are sealed with get-only properties and `internal` constructors.
   Dictionaries are read-only copies.
2. `ResponseReader.ReadSystemOne(byte[] body, string? requestId)` returns a `SystemOneResponse`.
   Follow the pattern in [Appendix A.10](#a10-responsereader):
   - Wrap `JsonDocument.Parse(body)` in `catch (JsonException)` and throw
     `TypeSafeResponseValidationException("$", "the body is not JSON")`. An empty body and a
     gateway's HTML page both land here. A root that is not an object (`[]`) throws with path
     `$` too.
   - One `Required` helper checks every required field and names its JSON path:
     `$.model` string, `$.usage.input_tokens` and `$.usage.output_tokens` integers,
     `$.answers` object.
   - Read token counts with `TryGetInt64`. A fractional or oversized value (`1.5`) throws
     `TypeSafeResponseValidationException` with the field path and "expected an integer".
     `GetInt64` would throw `FormatException` instead.
   - For each answer, `type` string. Then by kind:
     - `noul`: `noul` number.
     - `choice`: `choice` string, `confidence` number, `probabilities` object of numbers.
     - `score`: `score` and `confidence` numbers, `legend` object, `probabilities` object of
       numbers that is not empty (empty throws with path `$.answers.<id>.probabilities`). Legend
       and probability keys are parsed with `ResponseReader.LevelKey`; `Legend` values are
       `JsonElement.Clone()`.
     - anything else: `UnknownAnswer(type, answerElement.Clone())`.
   - `ScoreAnswer.MostLikelyLevel` is the key with the highest probability; on a tie, the lower
     level.
3. `SystemOneResponse.Answers` holds exactly what the server sent, nothing added or dropped.
   `GetNoul`, `GetChoice` and `GetScore`:
   - Throw `KeyNotFoundException` listing the ids the server did answer when the id is missing.
   - Throw `InvalidOperationException` naming the actual kind when the answer is another type.
   - The XML doc of each carries this line: "The server may leave a question unanswered; check
     `Answers.ContainsKey` first if that is possible for your questions." Plus the two
     `<exception cref>` tags.
4. `ResponseReader.ReadModels(byte[] body)` reads `$.models[]` into `ModelCard` (`name`,
   `description`, `release_date`, all strings), with the same non-JSON handling as step 2.
5. The rest of `TypeSafeModelFactory`, with the exact signatures in PLAN.md section 3.3
   (everything except `ApiException`, which T03 added). `ScoreAnswer` computes
   `MostLikelyLevel` itself.
6. Add the response fixtures from [Appendix B](#appendix-b-fixtures), including the malformed
   ones listed there.

**Tests.** `ResponseReaderTests`:
- Each documented fixture gives the expected values (AC-3.3).
- `type` as the last property still works.
- Each required field missing gives the right JSON path.
- `unknown-kind.json` gives `UnknownAnswer` with `Type == "ranking"` and its raw JSON, and the
  noul still parses (AC-3.6).
- `malformed-legend.json` gives path `$.answers.<id>.legend.one`.
- `malformed-empty.json`, `malformed-html.json` and `malformed-array.json` give path `$`;
  `malformed-fractional-tokens.json` gives `$.usage.input_tokens`; a score with
  `"probabilities": {}` gives `$.answers.<id>.probabilities`. None throws `JsonException` or
  `FormatException`.
- Accessor errors list the answered ids and name the actual kind.

**Done when.**
- [ ] AC-3.3 and AC-3.6 pass on both test targets.
- [ ] XML docs item (How to use, step 5), including the unanswered-question line.

## T07 Options, API key and settings snapshot

**Goal.** Public options types and the internal immutable settings the client runs on.

**Read first.** PLAN.md sections 1.4 and 3.4; ADR-0005; ADR-0008; Appendix A.2.

**Files.** `src/TypeSafeSharp/TypeSafeClientOptions.cs`, `RetryPolicy.cs`,
`TypeSafeRequestOptions.cs`, `src/TypeSafeSharp/Http/ApiKey.cs`,
`src/TypeSafeSharp/Http/ClientSettings.cs`, `src/TypeSafeSharp/Http/RetrySettings.cs`.

**Steps.**
1. Public types exactly as PLAN.md section 3.4, with `{ get; set; }`. `RetryPolicy` has two
   properties only, `MaxRetries` (default 2) and `RetryOnTimeout` (default true).
   `RetryPolicy.Default` is `public static RetryPolicy Default => new RetryPolicy();` and `None`
   returns `new RetryPolicy { MaxRetries = 0 }`, a new instance on every read.
2. `TypeSafeClientOptions.ToString()` returns
   `TypeSafeClientOptions { ApiKey = ***, BaseUrl = ..., DefaultModel = ... }`, with `***` when a
   key is set and `null` when not. Never the key.
3. `ApiKey.Resolve` and `ApiKey.Validate` from [Appendix A.2](#a2-apikey).
4. `RetrySettings` is internal sealed and immutable. `RetrySettings.From(RetryPolicy? policy)`
   copies `MaxRetries` (must be `>= 0`, else `ArgumentOutOfRangeException`) and
   `RetryOnTimeout`; null means `RetryPolicy.Default`. The rest are constants from ADR-0005,
   not options:

   ```csharp
   public static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(500);
   public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(5);
   public const double Jitter = 0.25;
   public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

   public static bool IsRetryableStatus(int status) => status == 408 || status == 429 || (status >= 500 && status <= 599);
   ```

   Connection errors are always retried. `retry-after-ms` wins over `Retry-After` (A.3).
5. `ClientSettings` is an internal sealed class with get-only properties, built by
   `ClientSettings.From(TypeSafeClientOptions options)`:
   - `ApiKey`: `ApiKey.Resolve(options.ApiKey)`.
   - `BaseUrl`: `options.BaseUrl`, else `TYPESAFE_BASE_URL` (trimmed, ignored when blank), else
     `https://api.typesafe.ai`. It must be absolute, with no user info and no query string; its
     scheme must be `https`, or `http` when `uri.IsLoopback` is true (this also covers
     `http://[::1]:8080`). Otherwise `TypeSafeConfigurationException`, whose message does not
     echo the URL. Strip trailing slashes from the path.
   - `DefaultModel`: `options.DefaultModel` if not blank, else `TYPESAFE_DEFAULT_MODEL` if not
     blank, else `jev-latest`.
   - `AttemptTimeout`, `TotalTimeout`: `Guard.Positive`. `Timeout.InfiniteTimeSpan` is rejected
     (it is -1 ms), and so is anything above `int.MaxValue` ms, which timers cannot take.
   - `Retry`: `RetrySettings.From(options.Retry)`.
   - `LoggerFactory`: the given one or `NullLoggerFactory.Instance`.
6. Per-call `TypeSafeRequestOptions` values go through the same checks (`Guard.Positive` for
   each timeout that is set, `RetrySettings.From` when `Retry` is set) when a call starts (T09).

**Tests.** `OptionsTests`:
- Precedence: code over environment over default, for base URL and model.
- Whitespace environment values are ignored.
- Key rules one by one, including an explicit empty key not falling back to a set environment
  variable, and the missing-key message containing `https://console.typesafe.ai/keys`.
- Environment handling: every test that reads the environment is in one collection declared
  with `[CollectionDefinition(DisableParallelization = true)]`, since the environment is
  process-wide. Its fixture saves and clears `TYPESAFE_API_KEY`, `TYPESAFE_BASE_URL` and
  `TYPESAFE_DEFAULT_MODEL` in the constructor and restores them in `Dispose`. A maintainer's
  machine often has a real key set; clearing it keeps "no key" tests honest and keeps the real
  key out of test output. Every other test sets `ApiKey = "ts_test_0000000000000000"`
  explicitly.
- `http://example.com` rejected; `http://localhost:8080` and `http://[::1]:8080` accepted.
- Timeouts: zero, negative, `Timeout.InfiniteTimeSpan` and `TimeSpan.MaxValue` throw
  `ArgumentOutOfRangeException`; `MaxRetries = -1` throws.
- `RetryPolicy.Default` returns a new instance on each read.
- `ToString()` and every exception message never contain the key (AC-3.4). Use a distinctive
  fake key such as `ts_test_ZZZZ_marker` and assert it is absent.
- AC-3.10: the options object is unchanged after `ClientSettings.From`, and changing it
  afterwards does not change the settings.

**Done when.**
- [ ] AC-3.4 and AC-3.10 pass.
- [ ] XML docs item (How to use, step 5); setters that validate late say where the check runs.

## T08 Error mapping and Retry-After

**Goal.** Turn a non-2xx response into the right exception with a safe message, read the
server's retry hint, and prove none of the parsing depends on the machine's culture.

**Read first.** ADR-0006 rule 1; PLAN.md sections 1.3 and 3.5; Appendix A.3 and A.6.

**Files.** `src/TypeSafeSharp/Errors/ErrorMessage.cs`, `src/TypeSafeSharp/Errors/ErrorMapper.cs`,
`src/TypeSafeSharp/Http/RetryTiming.cs`, `tests/TypeSafeSharp.Tests/ErrorMappingTests.cs`,
`tests/TypeSafeSharp.Tests/CultureTests.cs`.

**Steps.**
1. `ErrorMessage.Describe(int status, string? body)` from [Appendix A.6](#a6-errormessage).
2. Copy `RetryTiming` from [Appendix A.3](#a3-retrytiming). Keep the port comment that names
   the JS source file and tag. T09 uses `ParseRetryAfterMilliseconds` so a 429 carries
   `RetryAfter` even without retries; T11 uses `Backoff`.
3. `ErrorMapper.Create(int status, byte[] body, string endpoint, string? requestId,
   IReadOnlyDictionary<string, IReadOnlyList<string>> headers, double? retryAfterMs)`:
   - Decode the body as UTF-8 (`Encoding.UTF8.GetString`). Build the message with
     `ErrorMessage.Describe`.
   - 400 bad request, 401 authentication, 403 permission denied, 404 not found, 422
     unprocessable entity, 429 rate limit (`RetryAfter` from `retryAfterMs`), 529 overloaded,
     other 500 to 599 internal server, anything else (including a 3xx redirect) the base
     `TypeSafeApiException`.
   - For 422, parse `detail[]` into `ValidationError` items from `loc` (each segment as a
     string; numbers with `GetRawText()`), `msg` and `type`. Never read `input` or `ctx`.
4. The `headers` dictionary comes from the transport (T09) and holds response headers only, so
   it never contains `Authorization`.

**Tests.**
- `ErrorMappingTests`:
  - Each status gives its type.
  - FastAPI detail flattening, matching this example exactly:
    `"422 questions.urgency.score.criteria: List should have at least 1 item"`.
  - A JSON body with no message fields gives `"500 status code (no message in body)"`.
  - A 300-character plain-text body is cut to 200 characters plus `...`.
  - AC-3.12: a 422 whose `detail[0].input` is `"MARKER_STATE_TEXT"` produces an exception whose
    `Message`, `ToString()` and every `ValidationErrors` field do not contain the marker.
  - `RetryTiming` results listed under A.3, using `new HttpResponseMessage().Headers` with
    `TryAddWithoutValidation`.
- `CultureTests`, one `[Theory]` with `[InlineData("tr-TR")]` and `[InlineData("de-DE")]`. Each
  row sets `CultureInfo.CurrentCulture` and `CultureInfo.CurrentUICulture`, restores both in a
  `finally`, and checks: every request fixture writes the same bytes as under the invariant
  culture; every response fixture reads the same values; `Retry-After: 1.5` gives 1500 ms (under
  `de-DE` a culture-aware parse gives 15000); the 422 example message is exact.

**Done when.**
- [ ] AC-3.12 passes.
- [ ] `CultureTests` pass on both test targets.

## T09 Client transport and models

**Goal.** A working `TypeSafeClient` that sends one attempt per call, and `Models.ListAsync`.

**Read first.** PLAN.md section 3.1; ADR-0005 decisions 6 and 7; ADR-0008; Appendix A.4, A.5,
A.11; [Appendix C](#appendix-c-retry-loop) (read it all now; implement the loop in T11).

**Files.** `src/TypeSafeSharp/TypeSafeClient.cs`, `src/TypeSafeSharp/ModelsClient.cs`,
`src/TypeSafeSharp/AssemblyInfo.cs`, `src/TypeSafeSharp/Http/HttpDefaults.cs`,
`Transport.cs`, `SdkInfo.cs`, `CallSettings.cs`, `RawResponse.cs`, `Timing.cs`, `Jitter.cs` (all
under `src/TypeSafeSharp/Http/`), `tests/TypeSafeSharp.Tests/StubHandler.cs`,
`tests/TypeSafeSharp.Tests/ClientTests.cs`.

**Steps.**
1. Copy `Timing` ([Appendix A.4](#a4-timing)), `Jitter` ([Appendix A.5](#a5-jitter)) and
   `HttpDefaults` ([Appendix A.11](#a11-httpdefaults)), the file with the only `#if NET`. Its
   `AllowAutoRedirect = false` makes a redirect surface as a `TypeSafeApiException` instead of a
   POST silently turning into a GET.
2. Add `[assembly: System.Runtime.Versioning.UnsupportedOSPlatform("browser")]` to
   `AssemblyInfo.cs`. Blazor WebAssembly callers then get warning CA1416. Only the `net10.0`
   build keeps the attribute: on `netstandard2.0` PolySharp's copy is `[Conditional]` and the
   compiler drops it (checked with the stub). That is fine: .NET 10 WASM apps load the
   `net10.0` build, and the runtime check in step 4 covers everything else. Do not add a define
   to force it.
3. `SdkInfo`: `Version` from `AssemblyInformationalVersionAttribute` without any `+commit`
   suffix; `UserAgent` is `"TypeSafeSharp/" + Version`; `Runtime` is
   `RuntimeInformation.FrameworkDescription + " (" + os + "; " + RuntimeInformation.OSArchitecture
   + ")"`, where `os` is `windows`, `linux`, `osx` or `other` from
   `RuntimeInformation.IsOSPlatform`. Replace any character outside 0x20 to 0x7E with `_`, since
   header values must be ASCII. Example: `.NET 10.0.0 (linux; X64)`.
4. Constructors. All public ones call one internal constructor
   `TypeSafeClient(HttpClient httpClient, bool ownsHttpClient, TypeSafeClientOptions options,
   TimeProvider timeProvider, Func<double> jitter)`, passing `TimeProvider.System` and
   `Jitter.Next`:
   - Its first line is `internal static void ThrowIfBrowser(bool isBrowser)`, called with
     `RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER"))`. It throws
     `TypeSafeConfigurationException("TypeSafeClient runs server-side only; a browser app would expose the API key.")`.
     Then `ClientSettings.From(options)`.
   - `(string apiKey)` means `new TypeSafeClientOptions { ApiKey = apiKey }`.
   - `(TypeSafeClientOptions options)` builds the settings first, then creates and owns an
     `HttpDefaults.CreateClient()`. Creating it first would leak it when the options are invalid.
   - `(HttpClient httpClient, TypeSafeClientOptions options)` uses the given client and never
     disposes or changes it.
   - The protected parameterless constructor leaves everything null for mocks. On such an
     instance the virtual `SystemOneAsync` and `Models` throw `InvalidOperationException` unless
     a subclass overrides them.
5. `CallSettings` (internal sealed): `AttemptTimeout`, `TotalTimeout`, `RetrySettings Retry`,
   `string Endpoint`, `string? Model`, and `static CallSettings Resolve(ClientSettings settings,
   TypeSafeRequestOptions? options, string endpoint, string? model)`. A per-call value replaces
   the client value whole. `RawResponse` (internal sealed): `int StatusCode`, `byte[] Body`,
   `string? RequestId`, `IReadOnlyDictionary<string, IReadOnlyList<string>> Headers`.
6. `Transport.SendAsync(HttpMethod method, string path, byte[]? body, CallSettings call,
   CancellationToken ct)` returns a `RawResponse`. For T09 it runs one pass of Appendix C: no
   loop, no delay, and the `error` is thrown directly.
   - URI: `new Uri(settings.BaseUrl.AbsoluteUri.TrimEnd('/') + path)`, where `path` starts with
     `/`, so a gateway base path survives (`https://openrouter.ai/api` gives
     `https://openrouter.ai/api/v1/systemone`). `new Uri(baseUri, "/v1/...")` drops it.
   - Headers from PLAN.md section 3.1: `Authorization: Bearer <key>`, `Accept:
     application/json`, `User-Agent`, `X-TypeSafe-SDK`, `X-TypeSafe-Runtime` (`SdkInfo.Runtime`),
     and `request.Headers.ExpectContinue = false` (saves a round trip on .NET Framework), then
     `HttpDefaults.SetVersion(request)` (HTTP/2 on `net10.0`, PLAN.md section 3.1). The
     body is `ByteArrayContent` with `Content-Type: application/json`. `HttpClient` adds a
     caller's `DefaultRequestHeaders` only where the request has no header of that name, so SDK
     headers win.
   - `SendAsync(request, HttpCompletionOption.ResponseContentRead, token)` under the attempt
     timeout (A.4), then `ReadAsByteArrayAsync()`. Request id from `x-typesafe-request-id`,
     null when absent. Response and content headers copied into
     `Dictionary<string, IReadOnlyList<string>>` with `StringComparer.OrdinalIgnoreCase`.
   - Exceptions: the catch clauses of Appendix C, in that order. Non-2xx goes to
     `ErrorMapper.Create` with `RetryTiming.ParseRetryAfterMilliseconds(response.Headers,
     time.GetUtcNow())`.
   - The client has a `volatile bool disposed` field that `Transport` reads. A call on a
     disposed client throws `ObjectDisposedException(nameof(TypeSafeClient))` before sending.
7. `SystemOneAsync` overloads as PLAN.md section 3.1. The non-virtual ones call the virtual one,
   which validates the per-call options (T07 step 6), resolves
   `request.Model ?? settings.DefaultModel`, writes the body, sends `POST /v1/systemone`, and
   reads the response with `ResponseReader.ReadSystemOne`.
8. `ModelsClient` gets an internal constructor taking the transport. `ListAsync` sends
   `GET /v1/models` and returns `ResponseReader.ReadModels`. `TypeSafeClient.Models` returns
   one instance created in the constructor.
9. `Dispose()` calls `Dispose(true)` and `GC.SuppressFinalize(this)`. `Dispose(bool)` sets
   `disposed = true` first, then disposes the `HttpClient` only if the client owns it.
10. `StubHandler` in tests: a queue of responses or exceptions, a list of recorded requests
    (method, URI, version, version policy on `net10.0`, headers, body bytes read in `SendAsync`), an optional delay per response
    driven by `FakeTimeProvider`, and an option to wait until the request's token is cancelled.

**Tests.** `ClientTests` with `StubHandler`, through the internal constructor. Every test sets
`ApiKey = "ts_test_0000000000000000"`, so a real `TYPESAFE_API_KEY` on the developer's machine
never reaches the stub. Check `Authorization` with `Assert.True(actual == expected)`, never
`Assert.Equal`, so a wrong value is not printed.

- Documented request and response round trip; `Model` default and override; a base URL with a
  path (`https://gateway.example/api`) keeps it; `Models.ListAsync` parses the list.
- Every SDK header, no `X-TypeSafe-Retry-Count` on the first attempt, `ExpectContinue == false`,
  on `net10.0` `Version == 2.0` with `VersionPolicy == RequestVersionOrLower` and on `net481`
  `Version == 1.1` (the test reads `RuntimeInformation.FrameworkDescription`, not `#if`),
  and `X-TypeSafe-Runtime` matching `^.+ \((windows|linux|osx|other); [A-Za-z0-9]+\)$`.
- 401 gives `TypeSafeAuthenticationException` with `RequestId`; 302 gives a base
  `TypeSafeApiException` with `StatusCode == 302`; `HttpDefaults.CreateHandler()` has
  `AllowAutoRedirect == false` (read by reflection, so the test needs no `#if`).
- `HttpRequestException` from the handler gives `TypeSafeConnectionException`. So does
  `InvalidOperationException` (standing in for Polly's `TimeoutRejectedException`; do not
  reference Polly), with it as `InnerException`.
- The attempt timer (`FakeTimeProvider.Advance`) and `HttpClient.Timeout` on a caller's client
  both give `TypeSafeTimeoutException`.
- Caller cancellation: `Assert.ThrowsAnyAsync<OperationCanceledException>` (the runtime may
  throw `TaskCanceledException`) with `ex.CancellationToken == callerToken`, also when the
  handler throws `InvalidOperationException` after the caller cancelled.
- Dispose during a call: owned `new HttpClient(stub)` with `ownsHttpClient: true`, a stub that
  waits for cancellation, then `Dispose()`. The call throws `ObjectDisposedException` and the
  stub saw one request. A later call throws it too and sends nothing. `Dispose` disposes an
  owned client only.
- `ThrowIfBrowser(true)` throws `TypeSafeConfigurationException`; `false` does not.

**Done when.**
- [ ] The client works end to end against the stub on both targets.
- [ ] `new TypeSafeClient(options)` compiles in a `net481` project without a `System.Net.Http`
      reference (checked properly in T10).
- [ ] XML docs item (How to use, step 5) for every constructor, `SystemOneAsync` overload,
      `ListAsync` and `Dispose`.

**Traps.**
- `Environment.Version` is `4.0.30319.42000` on every .NET Framework; use
  `RuntimeInformation.FrameworkDescription`.
- On .NET Framework, a refused connection surfaces as `HttpRequestException` wrapping a
  `WebException`; catch `HttpRequestException`, not `WebException`.
- Do not set `HttpClient.Timeout` on a caller's client.
- `HttpClient.Dispose` cancels in-flight sends, which looks exactly like a timeout. That is why
  the `disposed` check comes before the timeout clause in Appendix C.
- The `catch (Exception ex) when (...)` clauses in Appendix C carry filters, which is what keeps
  CA1031 quiet (as in A.14). Do not suppress CA1031.
- CA1416 for the browser attribute only fires in projects that list `browser` as a supported
  platform, so the test and packaging projects see no warning.

Found while building T09 (2026-10-06):
- `HttpClient.SendAsync` does not check an already-cancelled token before calling the handler,
  so `Transport.SendAsync` calls `ThrowIfCancellationRequested()` first. Without it a cancelled
  call still sends.
- The core csproj lists `<SupportedPlatform Include="browser" />`; without it CA1418 rejects
  `UnsupportedOSPlatform("browser")` on `netstandard2.0`.
- Two suppressions, each with its reason in the code: RS0026 on the `SystemOneAsync` overloads
  (the shapes in PLAN.md section 3.1 are deliberate), and CA2016 on `ReadAsByteArrayAsync()`
  (no token overload on `netstandard2.0`).
- The tests also build for `net481`, so `.editorconfig` turns off CA1849 and CA2016 under
  `tests/`. xUnit1051 is on: pass `TestContext.Current.CancellationToken` to every call that
  takes a token.
- `StubHandler.WaitForRequestAsync` counts requests with a semaphore. A one-shot signal swapped
  per request races: the request often arrives before the test starts waiting.

## T10 Packaging consumers

**Goal.** Prove the packed packages work for an AOT app and a C# 7.3 .NET Framework app, on
every PR.

**Read first.** PLAN.md sections 4.2 and 4.4; ADR-0012 decision 5; ada-csharp
`tests/packaging/verify-package.sh`.

**Files.** `tests/packaging/verify-package.sh`, `tests/packaging/consumers/Aot/*`,
`tests/packaging/consumers/NetFx/*`, `ci.yml` (new `packaging` job).

**Steps.**
1. Consumers live outside the solution and restore from a temporary folder feed, like ada's
   script. Each has its own `NuGet.Config` that maps `TypeSafeSharp*` to the folder and `*` to
   nuget.org, and sets a fresh `NUGET_PACKAGES` directory so a stale cached package can never be
   used. Each consumer folder also has an empty `Directory.Build.props` (`<Project />`) and a
   `Directory.Packages.props` with
   `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>`, so nothing from the
   repository root (central versions, signing, analyzers, `Version`) is inherited.
2. Each consumer runs one call against a local stub: an in-process `HttpListener` on
   `http://localhost:<free port>/` (get a free port by starting a `TcpListener` on port 0 and
   reading its port) that answers `POST /v1/systemone` with
   `tests/TypeSafeSharp.Tests/Fixtures/responses/noul.json` and header
   `x-typesafe-request-id: req_test_0001`. `http://` is allowed for loopback (T07). The
   consumer hard-codes the fake key `ts_test_0000000000000000`; it never reads
   `TYPESAFE_API_KEY`.
   - `Aot` (`net10.0`, `PublishAot=true`, `TreatWarningsAsErrors=true`,
     `<TrimmerRootAssembly Include="TypeSafeSharp" />`): asks one Noul and one plain
     `Question.Choice(instructions, "billing", "technical")` question, then `dotnet publish` and
     run the native binary.
   - `NetFx` (`net481`, `<LangVersion>7.3</LangVersion>`, no `System.Net.Http` reference): a
     `static async Task<int> Main()` that builds `new TypeSafeClient(options)`, sets
     `request.Model` and `options.Retry.MaxRetries`, and makes the call. Windows only.
3. `verify-package.sh` packs to a temp folder with `-p:Version=0.0.0-ci.<run>`, then builds and
   runs each consumer that fits the OS. With `--packages <dir>` it skips packing and uses the
   `.nupkg` files in that folder (T17 needs this).
4. `ci.yml`: a `packaging` job with a matrix of ubuntu-24.04 (Aot) and windows-2022 (NetFx),
   running the script with `shell: bash`.

**Done when.**
- [ ] AC-4.3 (console part) and AC-4.4 pass in CI.
- [ ] Paste the dependency groups of both packed `.nuspec` files into the PR description. The
      core groups list only the packages in PLAN.md section 4.2 for each target, and the DI
      groups list `TypeSafeSharp`, `Microsoft.Extensions.Http` and
      `Microsoft.Extensions.Options.ConfigurationExtensions`. The maintainer repeats this check
      before each release (runbook).

**Traps.** NativeAOT needs the platform linker: `clang` on Linux runners is present; on a
Windows desktop without the C++ workload, publish fails with "Platform linker not found". That
is an environment problem, not a code problem.

## T11 Retry loop, timeouts and budget

**Goal.** The retry behaviour from ADR-0005, exactly, testable in milliseconds.

**Read first.** ADR-0005; PLAN.md section 5.2; Appendix C; Appendix A.3 (`Backoff`), A.4, A.5;
`typesafe-sdk-js` `src/retry.ts` and `src/client.ts` at tag `v0.6.0`.

**Files.** `src/TypeSafeSharp/Http/Transport.cs` (add the loop),
`tests/TypeSafeSharp.Tests/RetryTests.cs`.

**Steps.**
1. Wrap the single attempt from T09 in the loop in [Appendix C](#appendix-c-retry-loop). The
   call settings resolve once per call: per-call `Retry`, `AttemptTimeout` and `TotalTimeout`
   replace the client's (they do not merge field by field).
2. Leave out the `Log.*` lines of Appendix C for now; T12 adds them.

**Tests.** `RetryTests`: one `[Theory]` row per scenario in PLAN.md section 5.2, driven by
`FakeTimeProvider` and a jitter source returning 0 (or 1 for the "minus jitter" row). Assert
attempt count, the `X-TypeSafe-Retry-Count` header values, the delays (advance the fake clock
by exactly the expected delay and check the next attempt starts, and not one tick earlier), and
the final exception type. Add two rows outside the table: a handler-chain exception
(`InvalidOperationException`) is not retried, and disposing the owned client during a retry
delay ends the call with `ObjectDisposedException` and no further attempt.

**Done when.**
- [ ] AC-5.2: every row in PLAN.md section 5.2 is a test and passes on both targets.
- [ ] The retry tests run in under 2 seconds in total (no real sleeping).

**Traps.**
- Throw the API error you created only where Appendix C says; a `catch (TypeSafeException)`
  around the attempt would swallow cancellation logic.
- Dispose each `HttpResponseMessage` and each attempt's `CancellationTokenSource` and timer
  (CA2000 will remind you).
- `serverDelay` is reset to null at the start of each attempt, so a timeout after a 429 does not
  reuse the old hint.

## T12 Logging and tracing

**Goal.** Log events 1 to 5 from ADR-0010 and one activity per `SystemOneAsync` call, with
nothing sensitive in either and no cost when nobody listens.

**Read first.** ADR-0008 decision 4; ADR-0010; Appendix A.12 and A.13.

**Files.** `src/TypeSafeSharp/Diagnostics/Log.cs`, `src/TypeSafeSharp/Diagnostics/Telemetry.cs`,
call sites in `Transport.cs` and `TypeSafeClient.cs`, `tests/TypeSafeSharp.Tests/LoggingTests.cs`,
`tests/TypeSafeSharp.Tests/TelemetryTests.cs`.

**Steps.**
1. `Log` from [Appendix A.12](#a12-log): a `static partial class` with `[LoggerMessage]`
   methods for events 1 to 5, using `{Endpoint}` (`"POST /v1/systemone"` or
   `"GET /v1/models"`). Logger category `TypeSafeSharp`
   (`loggerFactory.CreateLogger("TypeSafeSharp")`). Add the `Log.*` calls where Appendix C
   marks them.
2. Event 5 (call failed) is `Warning`, not `Error`: callers decide what is an error, and the web
   sample turns a 429 into a 503 on purpose. Its `StatusCode` argument is the status as a
   string, or `"timeout"` or `"connection error"`.
3. Event 4 (unknown answer kind) is logged by the client after `ReadSystemOne`, once per kind
   per client: keep a `ConcurrentDictionary<string, bool>` of kinds already reported.
4. Headers and bodies are never logged. Never pass the body, headers, state, instructions or
   the key to any log call.
5. `Telemetry` from [Appendix A.13](#a13-telemetry). In the virtual `SystemOneAsync`:
   `using var activity = Telemetry.StartSystemOne(model, settings.BaseUrl);`, then
   `Telemetry.Succeeded` or, in a `catch` that rethrows with `throw;`, `Telemetry.Failed`.
   `Models.ListAsync` gets no activity in 0.1 (not a GenAI operation). There are no metrics in
   0.1 (ADR-0010).

**Tests.**
- `LoggingTests` with a capturing `ILoggerProvider` written in the test project:
  - Each event id and level fires in the right situation; event 5 is `Warning`.
  - At `Trace` level, no captured message or state contains the fake key, an `Authorization`
    value, or a marker string placed in the request state and instructions.
- `TelemetryTests` with an `ActivityListener` on source `TypeSafeSharp`:
  - The tag set from A.13 on success, and on a 429 failure (`error.type == "429"`, status
    `Error`).
  - AC-3.9: no tag value contains the state or instruction marker text.
  - With no listener, `StartActivity` returns null and nothing throws.

**Done when.**
- [ ] Events 1 to 5 exist with the ids, levels and templates of A.12, and A.12 matches the
      ADR-0010 table. If they differ, stop and report.
- [ ] AC-3.9 passes.

## T13 Batch helper

**Goal.** `EvaluateManyAsync` as PLAN.md section 3.6.

**Read first.** PLAN.md section 3.6; ADR-0007 decision 3; Appendix A.14.

**Files.** `src/TypeSafeSharp/BatchOptions.cs`, `BatchItem.cs`, `TypeSafeClient.cs` (the two
overloads), `src/TypeSafeSharp/Http/BatchRunner.cs`, `tests/TypeSafeSharp.Tests/BatchTests.cs`.

**Steps.**
1. Public overloads validate eagerly: `items` and `createRequest` not null, `MaxConcurrency >=
   1`. Then return the core iterator. See [Appendix A.14](#a14-batch-loop).
2. Each item: call `createRequest(item)`, then the virtual
   `SystemOneAsync(request, options.RequestOptions, token)`. Measure `Elapsed` from just before
   `createRequest` to completion with `TimeProvider`, so queue time is excluded.
3. Result handling:
   - Success: `BatchItem` with `Response`.
   - `TypeSafeAuthenticationException`, `TypeSafePermissionDeniedException`,
     `TypeSafeNotFoundException`, `TypeSafeConfigurationException`: rethrow; this ends the
     enumeration and the `finally` block cancels the other in-flight calls.
   - Any other `TypeSafeException`, or an `ArgumentException` (from `createRequest` or request
     validation): `BatchItem` with `Exception`.
   - `OperationCanceledException` and `ObjectDisposedException`: propagate.
4. `Index` is the zero-based position of the item in `items`.

**Tests.** `BatchTests` with a counting `StubHandler` that tracks concurrent calls:
- Never more than `MaxConcurrency` in flight, and that many are reached.
- Completion order, with `Index` matching the input.
- A 422 on one item comes back as a failed item; the others succeed.
- A 401 ends the enumeration with `TypeSafeAuthenticationException`.
- Cancellation stops within 100 ms (fake clock plus a real short wait for the tasks to observe
  it) and throws `OperationCanceledException`.
- Breaking out of `await foreach` after the first item cancels in-flight calls (the handler sees
  cancelled tokens).
- Argument errors throw at the call, before enumeration.
- A mock that overrides only the virtual `SystemOneAsync` drives the batch (subclass the client
  through the protected constructor).

**Done when.**
- [ ] AC-3.7 passes.
- [ ] XML docs item (How to use, step 5): both overloads name the fatal types that end the
      enumeration.

## T14 Dependency injection package

**Goal.** `AddTypeSafe` as ADR-0009, AOT-safe, with two overloads.

**Read first.** ADR-0009; PLAN.md section 3.7.

**Files.** `src/TypeSafeSharp.Extensions.DependencyInjection/TypeSafeServiceCollectionExtensions.cs`,
its tests, `tests/packaging/consumers/AotWeb/*`.

**Steps.**
1. Two overloads, each returning the `IHttpClientBuilder` of the named client `"TypeSafeSharp"`:
   - `AddTypeSafe(this IServiceCollection services, IConfiguration configuration)`:
     `services.AddOptions<TypeSafeClientOptions>("TypeSafeSharp").Bind(configuration)`.
   - `AddTypeSafe(this IServiceCollection services, Action<TypeSafeClientOptions> configure)`:
     `.Configure(configure)`.
2. On the options builder:
   `.Validate(o => HasKey(o), "No TypeSafe API key. Set TypeSafe:ApiKey in configuration or the TYPESAFE_API_KEY environment variable. Create a key at https://console.typesafe.ai/keys.")`
   then `.ValidateOnStart()`. `HasKey` passes when `o.ApiKey ?? TYPESAFE_API_KEY` passes
   `ApiKey.Validate` (visible to this assembly through the `InternalsVisibleTo` from T01).
   Validation failures never include the key.
3. The named client: `ConfigurePrimaryHttpMessageHandler(HttpDefaults.CreateHandler)`,
   `SetHandlerLifetime(Timeout.InfiniteTimeSpan)`, and `ConfigureHttpClient(c => c.Timeout =
   Timeout.InfiniteTimeSpan)`. A singleton captures one `HttpClient`, so handler rotation would
   never reach it (ADR-0009); `HttpDefaults` rotates connections instead.
4. The client registration: a singleton factory that resolves `IHttpClientFactory`,
   `IOptionsMonitor<TypeSafeClientOptions>.Get("TypeSafeSharp")` and `ILoggerFactory`, sets
   `LoggerFactory` on a copy of the options (not on the monitored instance), and constructs
   `new TypeSafeClient(httpClient, options)`.
5. `EnableConfigurationBindingGenerator` is already on (T01); the `net10.0` build must show no
   IL warnings. If it does, the binding call is not being intercepted: check the property is in
   this project's csproj.
6. `AotWeb` packaging consumer: an ASP.NET Core `net10.0` app with `PublishAot=true` that calls
   `AddTypeSafe(builder.Configuration.GetSection("TypeSafe"))` and publishes with warnings as
   errors (AC-4.3 second half). Same folder rules as T10 step 1. Add it to `verify-package.sh`
   on the Linux leg.

**Tests.** DI tests with `HostApplicationBuilder` (from `Microsoft.Extensions.Hosting`, a
test-only package in PLAN.md section 4.2) and in-memory configuration. The environment
collection from T07's tests applies here too.

- Binding of every property, including nested `Retry` (`MaxRetries`, `RetryOnTimeout`) and
  timeouts written as `"00:00:20"`.
- AC-3.8: no key anywhere means `host.StartAsync()` throws `OptionsValidationException`, while
  `Build()` does not. A key only in `TYPESAFE_API_KEY` starts fine.
- The client is a singleton.
- The named client's primary handler: walk `InnerHandler` from
  `IHttpMessageHandlerFactory.CreateHandler("TypeSafeSharp")` to the last handler and check
  `AllowAutoRedirect == false` by reflection.

**Done when.**
- [ ] AC-3.8 and the AOT web part of AC-4.3 pass.
- [ ] XML docs item (How to use, step 5) for both overloads.

## T15 READMEs

**Goal.** The READMEs users see on GitHub and nuget.org, telling the truth about limits.

**Read first.** The writing-style skill; ADR-0001 (support matrix); ADR-0009 (resilience);
`sample/`.

**Files.** `README.md`, `src/TypeSafeSharp/PACKAGE.md`,
`src/TypeSafeSharp.Extensions.DependencyInjection/PACKAGE.md`.

**Steps.**
1. Change the root README status from "planning" to the real state. Copy code from `sample/`
   rather than writing it fresh, so it is code that compiles. A block that needs a
   package outside PLAN.md section 4.2 starts with a `// fragment: needs <package>` comment.
2. Add these root README sections or lines:
   - Thread safety: "Create one `TypeSafeClient` per app and share it; it is thread-safe. Do
     not create one per request."
   - Security: "Server-side only. Do not ship a key inside a WASM, MAUI or desktop app you
     distribute."
   - Support matrix: `netstandard2.0` supports .NET Framework 4.7.2 and later (tested on
     4.8.1); 4.6.2 may work but is unsupported (its support ends 2027-01-12); .NET 8 and 9 until
     their end of support on 2026-11-10. Use the same wording as ADR-0001.
   - Resilience recipe, for callers with their own pipeline:

     ```csharp
     // fragment: needs Microsoft.Extensions.Http.Resilience
     builder.Services
         .AddTypeSafe(o =>
         {
             o.Retry = RetryPolicy.None;
             o.AttemptTimeout = TimeSpan.FromSeconds(60);   // longer than the pipeline's total
             o.TotalTimeout = TimeSpan.FromSeconds(60);
         })
         .AddStandardResilienceHandler();
     ```

   - Testing your code, a hand-written fake through the protected constructor:

     ```csharp
     sealed class FakeTypeSafeClient : TypeSafeClient
     {
         public override Task<SystemOneResponse> SystemOneAsync(
             SystemOneRequest request, TypeSafeRequestOptions? options, CancellationToken cancellationToken = default)
             => Task.FromResult(TypeSafeModelFactory.SystemOneResponse(
                 "jev-1.13.0",
                 new Dictionary<string, Answer> { ["is_urgent"] = TypeSafeModelFactory.NoulAnswer(0.95) },
                 TypeSafeModelFactory.Usage(296, 20)));
     }
     ```

   - Versioning and support: only the latest minor gets fixes; after 1.0 a TypeSafe wire change
     that forces a public API change is a major bump; target frameworks are dropped only in a
     minor (0.x) or major (1.x and later) release with one release of notice; if TypeSafe ships
     an official .NET SDK, this package is deprecated on nuget.org pointing to it. CHANGELOG
     entries that break start with `**Breaking:**`.
   - Recipes, one or two lines each: enum options with `Enum.GetNames(typeof(T))` and
     `Enum.Parse`; typed state with `JsonSerializer.SerializeToNode(value,
     MyContext.Default.MyType)`; extra headers through your own `HttpClient` and
     `DefaultRequestHeaders`; a second client with `AddKeyedSingleton`; raw question types with
     `Question.FromJson`; the C# 7.3 `MoveNextAsync` loop for `EvaluateManyAsync`; Aspire
     (`AddParameter("typesafe-key", secret: true)` and
     `.WithEnvironment("TYPESAFE_API_KEY", ...)`).
3. Write both `PACKAGE.md` files from the root README: what it is, install, the first example,
   Limits, and links. Every link absolute
   (`https://github.com/sanamhub/typesafe-dotnet/blob/main/...`), since nuget.org renders them
   outside the repository.

**Done when.**
- [ ] Every item in step 2 is in the root README, and each fact appears once (the package
      READMEs link to it).
- [ ] Every non-fragment block builds when pasted into a new console or web project that
      references the packed packages (check by hand once, list the blocks in the PR).
- [ ] The writing-style final pass is done: no em dashes, no banned words.

## T16 API drift check and scheduled live tests

**Goal.** Hear about schema changes from a nightly job with no secret, and about behaviour
changes from a weekly live run with a CI key that no pull request can read.

**Read first.** ADR-0012 decisions 4 and 6; `docs/runbooks/release.md`.

**Files.** `tests/TypeSafeSharp.Tests/Live/LiveTests.cs`, `tests/contract/openapi.snapshot.json`,
`.github/workflows/live.yml`.

**Steps.**
1. `LiveTests` are marked `[Trait("Category", "Live")]`. They read `TYPESAFE_API_KEY` from the
   environment and fail (not skip) when it is missing. About ten requests: one per question type,
   the models list, a structured state, a pinned model, an unknown model name (expect a
   `TypeSafeApiException` with a 4xx status and a request id), and a batch of three. Synthetic
   text only. They are not in the environment collection from T07, and `ci.yml` never runs
   them.
2. Save today's `https://api.typesafe.ai/openapi.json` as `tests/contract/openapi.snapshot.json`,
   normalised with `python3 -m json.tool --sort-keys`.
3. `live.yml` triggers: `schedule` with two crons, `'0 3 * * *'` (nightly) and `'0 4 * * 1'`
   (Mondays), plus `workflow_dispatch`. `permissions: contents: read` at the top. Never
   `pull_request` or `pull_request_target`.
4. Job `openapi-drift`, on every trigger: no environment, no secret. It downloads the spec with
   `curl -fsSL`, normalises it the same way, and runs `diff -u` against the snapshot. On a
   difference it writes the diff to `$GITHUB_STEP_SUMMARY` and fails; GitHub mails the owner.
5. Job `live-tests`, only when `github.event.schedule == '0 4 * * 1'` or
   `github.event_name == 'workflow_dispatch'`: `environment: live`, `ubuntu-24.04`, timeout 10
   minutes, `concurrency: live` without cancelling. The test step alone gets
   `env: TYPESAFE_API_KEY: ${{ secrets.TYPESAFE_API_KEY }}` and runs
   `dotnet test -c Release -f net10.0 -- --filter-trait "Category=Live"`. Never echo the key,
   never pass it on a command line, and never add a step that prints the environment.
6. An agent never creates the key, never sets the secret, and never asks for either. The
   maintainer adds the secret to environment `live` (T18 step 1) before the first run.

**Done when.**
- [ ] A manual run of `live.yml` is green in both jobs (after the maintainer has set the secret).
- [ ] Dispatching `live.yml` from a branch other than `main` runs `openapi-drift` but
      `live-tests` is refused by the `live` environment's branch rule.
- [ ] Running the live filter locally with no `TYPESAFE_API_KEY` fails with the "key missing"
      message rather than passing.
- [ ] The live tests build, and the CI filter excludes them (the test count in the CI log does
      not include them).

## T17 Release pipeline

**Goal.** `release.yml` as PLAN.md section 6.2, able to run as a dry run.

**Read first.** PLAN.md section 6.2; `docs/runbooks/release.md`; ada-csharp `release.yml` and
`scripts/changelog-section.sh`.

**Files.** `.github/workflows/release.yml`, `scripts/changelog-section.sh`.

**Steps.**
1. Start from ada-csharp's files and remove natives, checksums, SBOM and signing. Triggers:
   push of tags `v*`, and `workflow_dispatch` (always a dry run).
2. `preflight` (`permissions: contents: read`) reads `<Version>` from `Directory.Build.props`
   (ada reads a csproj; change it) and fails unless the tag (on a tag run) equals `v<Version>`,
   `CHANGELOG.md` has a section for that version, and `PublicAPI.Unshipped.txt` is empty in both
   projects. Skip the Unshipped check when `<Version>` contains `-` (a prerelease), since the
   API moves to Shipped at 0.1.0 (T18).
3. `verify` on ubuntu-24.04 packs once, checks both file names carry the version
   (`TypeSafeSharp.<v>.nupkg`, `TypeSafeSharp.Extensions.DependencyInjection.<v>.nupkg`), runs
   `verify-package.sh --packages <dir>` on those files, and uploads them with
   `actions/upload-artifact`. `verify-netfx` on windows-2022 downloads the artifact and runs the
   script the same way for the NetFx consumer.
4. `publish`: `needs: [verify, verify-netfx]`, `runs-on: ubuntu-24.04`,
   `environment: production`, permissions `contents: write`, `id-token: write`,
   `attestations: write`, and `if: github.event_name == 'push'` on the whole job. The
   environment only deploys from `v*` tags, so a step-level condition would still be refused on
   `main`; a dry run ends after `verify`. Steps:
   1. Download the artifact. Never re-pack.
   2. `uses: NuGet/login@<sha>` with `id: login` and `user: ${{ secrets.NUGET_USER }}`.
   3. For core, then DI: `dotnet nuget push <file> --api-key ${{ steps.login.outputs.NUGET_API_KEY }}
      --source https://api.nuget.org/v3/index.json --skip-duplicate`, stopping on the first
      failure. The key is the one-hour key from the login step. Never create or reference a
      `NUGET_API_KEY` secret (ADR-0011).
   4. `actions/attest` with `subject-path` both `.nupkg` files.
   5. Create the release with notes from `changelog-section.sh`.
5. `concurrency: { group: release-${{ github.ref }}, cancel-in-progress: false }`. Every
   action pinned to a full SHA with a version comment; `persist-credentials: false` on every
   checkout.

**Done when.**
- [ ] A `workflow_dispatch` dry run is green through `verify` and `verify-netfx`, skips
      `publish`, and pushes nothing.

## T18 HUMAN: first prerelease, then 0.1.0

**Goal.** Reserve the package IDs with `0.1.0-alpha.1`, then ship `0.1.0`.

**Steps.**
1. One-time setup, PLAN.md section 6.3: environment `production` (deploys from `v*` tags only,
   the maintainer as required reviewer); a ruleset that restricts who can create or delete `v*`
   tags; the nuget.org trusted publishing policy (owner `sanamhub`, repository
   `typesafe-dotnet`, workflow `release.yml`, environment `production`, package scope
   `TypeSafeSharp*`); repository secret `NUGET_USER` (the nuget.org user name, not a key);
   environment `live` (deploys from `main` only, no reviewer) with secret `TYPESAFE_API_KEY`
   set to the CI key from T00.
2. Before every tag, from the runbook: a green `live-tests` run within 24 hours (dispatch
   `live.yml` if needed); a green `openapi-drift` run this week; open both packed `.nupkg` files and check the dependency
   groups match PLAN.md section 4.2; run the release dry run.
3. Set `<Version>0.1.0-alpha.1</Version>`, add its CHANGELOG section, merge, tag
   `v0.1.0-alpha.1`, push, and approve the `production` deployment.
4. Post-deploy check (org rule 12): the nuget.org pages show "Unofficial" in the description
   and no TypeSafe logo; the sample builds against the published version with
   `-p:TypeSafeSharpVersion=0.1.0-alpha.1`; Quickstart runs once with your own key. If a check
   fails, follow the runbook's "Rolling back" section.
5. After alpha feedback: fix what it turned up, move all `PublicAPI.Unshipped.txt` entries to
   `PublicAPI.Shipped.txt`, set `<Version>0.1.0</Version>`, and repeat steps 2 to 4 for
   `v0.1.0`. Then open a new `Unreleased` CHANGELOG section.

**Done when.** `0.1.0` is on nuget.org, the post-deploy check passed, and the sample's pinned
version resolves from nuget.org with its default `NuGet.Config`.

## Appendix A: reference code

Every block below, in its final form, was compiled on 2026-09-24 for `netstandard2.0` and
`net10.0` with the T01 analyzer settings (warnings as errors, AOT analysis on `net10.0`), next
to minimal stand-ins for the types other tasks create. The helpers in A.1 to A.6, A.9, A.11 and
A.14 were also run on `net10.0` and `net481`. The namespaces and `using` directives are
complete. Add XML docs only where a type becomes public. A.7 and A.8 were removed.

### A.0 `.editorconfig` block for `src`

```ini
[src/**.cs]
# Multi-targeting (ADR-0001). These rules fire on net10.0 and suggest APIs netstandard2.0 does
# not have. Following them would need #if for behaviour-neutral code, which ADR-0001 forbids.
dotnet_diagnostic.CA1510.severity = none   # ArgumentNullException.ThrowIfNull
dotnet_diagnostic.CA1511.severity = none   # ArgumentException.ThrowIfNullOrEmpty
dotnet_diagnostic.CA1512.severity = none   # ArgumentOutOfRangeException.ThrowIf*
dotnet_diagnostic.CA1513.severity = none   # ObjectDisposedException.ThrowIf
dotnet_diagnostic.CA1845.severity = none   # span-based string.Concat
dotnet_diagnostic.CA1846.severity = none   # AsSpan instead of Substring
dotnet_diagnostic.CA1847.severity = none   # string.Contains(char)
dotnet_diagnostic.CA1849.severity = none   # CancellationTokenSource.CancelAsync
dotnet_diagnostic.CA1865.severity = none   # char overloads of StartsWith, EndsWith, IndexOf
dotnet_diagnostic.CA1866.severity = none
dotnet_diagnostic.CA1867.severity = none
dotnet_diagnostic.CA2249.severity = none   # string.Contains(string, StringComparison)
```

### A.1 Guard

Edited after the check: `Positive` no longer accepts `Timeout.InfiniteTimeSpan` and gained the
upper bound.

```csharp
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TypeSafeSharp;

// One guard class for both targets, so argument checks need no #if (ADR-0001).
internal static class Guard
{
    public static T NotNull<T>([NotNull] T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class
        => value ?? throw new ArgumentNullException(name);

    // Timers take at most int.MaxValue ms. Timeout.InfiniteTimeSpan is -1 ms, so it fails the first test.
    public static TimeSpan Positive(TimeSpan value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => value > TimeSpan.Zero && value.TotalMilliseconds <= int.MaxValue
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "Must be greater than zero and at most 24 days.");
}
```

### A.2 ApiKey

Edited after the check: the missing-key message ends with the console URL.

```csharp
using System;

namespace TypeSafeSharp;

// Ported from typesafe-sdk-python v0.7.1: strip outer whitespace, then reject what HttpClient
// would reject later with a confusing error. Messages name the rule and never echo the key.
internal static class ApiKey
{
    public const string EnvironmentVariable = "TYPESAFE_API_KEY";

    public static string Resolve(string? fromOptions)
    {
        if (fromOptions is not null)
        {
            // An explicit empty key is a mistake, not a request to read the environment.
            return Validate(fromOptions);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(fromEnvironment))
        {
            throw new TypeSafeConfigurationException(
                "No API key was provided. Set TypeSafeClientOptions.ApiKey or the TYPESAFE_API_KEY environment variable. Create a key at https://console.typesafe.ai/keys.");
        }

        return Validate(fromEnvironment!);
    }

    public static string Validate(string key)
    {
        var trimmed = key.Trim();
        if (trimmed.Length == 0)
        {
            throw new TypeSafeConfigurationException("The API key is empty.");
        }

        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                throw new TypeSafeConfigurationException("The API key contains whitespace inside it. Check for a pasted line break.");
            }

            if (char.IsControl(c))
            {
                throw new TypeSafeConfigurationException("The API key contains a control character.");
            }

            if (c > '~')
            {
                throw new TypeSafeConfigurationException("The API key contains a non-ASCII character. Check for a pasted quote or symbol.");
            }
        }

        return trimmed;
    }
}
```

### A.3 RetryTiming

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;

namespace TypeSafeSharp;

// Ported from typesafe-sdk-js v0.6.0 src/retry.ts (parseRetryAfter, retryDelayMs).
internal static class RetryTiming
{
    // Milliseconds as double so a huge header value cannot overflow TimeSpan.
    public static double? ParseRetryAfterMilliseconds(HttpResponseHeaders headers, DateTimeOffset now)
    {
        if (TryGetFirst(headers, "retry-after-ms", out var msText)
            && double.TryParse(msText, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            && ms >= 0 && !double.IsInfinity(ms))
        {
            return ms;
        }

        if (!TryGetFirst(headers, "Retry-After", out var raw))
        {
            return null;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds >= 0 && !double.IsInfinity(seconds) ? seconds * 1000 : null;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return Math.Max(0, (date - now).TotalMilliseconds);
        }

        return null;
    }

    // retryIndex is zero based: the first retry waits about InitialBackoff.
    public static TimeSpan Backoff(int retryIndex, TimeSpan initial, TimeSpan max, double jitter, double random01)
    {
        var exponential = Math.Min(initial.TotalMilliseconds * Math.Pow(2, retryIndex), max.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Round(exponential * (1 - (random01 * jitter)), MidpointRounding.AwayFromZero));
    }

    private static bool TryGetFirst(HttpResponseHeaders headers, string name, out string value)
    {
        if (headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            var first = values.FirstOrDefault();
            if (first is not null)
            {
                value = first.Trim();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
```

Checked results: `Retry-After: 2` gives 2000; `retry-after-ms: 250` wins over `Retry-After: 9`;
an HTTP date 3 s ahead gives about 3000; `1.5` gives 1500. `Backoff(0, 500 ms, 5 s, 0.25, 0)` is
500 ms, `Backoff(1, ..., 1)` is 750 ms, `Backoff(9, ..., 0)` is capped at 5 s.

### A.4 Timing

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TypeSafeSharp;

// TimeProvider helpers that exist on both targets. On net10.0 Task.Delay has a TimeProvider
// overload, but netstandard2.0 does not, and ADR-0001 allows no #if for this.
internal static class Timing
{
    public static async Task DelayAsync(TimeProvider timeProvider, TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => done.TrySetCanceled(cancellationToken)))
        using (timeProvider.CreateTimer(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), done, delay, Timeout.InfiniteTimeSpan))
        {
            await done.Task.ConfigureAwait(false);
        }
    }

    // Cancels the source after the delay. Dispose the returned timer before the source.
    public static ITimer CancelAfter(TimeProvider timeProvider, CancellationTokenSource source, TimeSpan delay)
        => timeProvider.CreateTimer(
            static state =>
            {
                try
                {
                    ((CancellationTokenSource)state!).Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The attempt finished and disposed its source while the timer fired.
                }
            },
            source,
            delay,
            Timeout.InfiniteTimeSpan);
}
```

A cancelled delay throws `OperationCanceledException` carrying the caller's token (checked on
both runtimes). `Timeout.InfiniteTimeSpan` here is the timer's period (fire once), not a
timeout.

### A.5 Jitter

```csharp
using System;

namespace TypeSafeSharp;

// Random.Shared is not on netstandard2.0, and System.Random is not thread-safe.
internal static class Jitter
{
    private static readonly object Gate = new();
#pragma warning disable CA5394 // Backoff jitter spreads retries; it is not a security decision.
    private static readonly Random Source = new();

    public static double Next()
    {
        lock (Gate)
        {
            return Source.NextDouble();
        }
    }
#pragma warning restore CA5394
}
```

### A.6 ErrorMessage

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace TypeSafeSharp;

// Ported from typesafe-sdk-js v0.6.0 src/errors.ts (extractMessage, describeValidationErrors),
// minus raw JSON echo: a body we cannot read a message from may hold customer state.
internal static class ErrorMessage
{
    private const int MaxRawText = 200;

    public static string Describe(int status, string? body)
    {
        var prefix = status.ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(body))
        {
            return prefix + " status code (no body)";
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body!);
        }
        catch (JsonException)
        {
            // Plain text, usually from a proxy. Short and capped.
            var text = body!.Length > MaxRawText ? body.Substring(0, MaxRawText) + "..." : body;
            return prefix + " " + text;
        }

        using (document)
        {
            var detail = Extract(document.RootElement);
            return detail is null ? prefix + " status code (no message in body)" : prefix + " " + detail;
        }
    }

    private static string? Extract(JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.String)
        {
            return NullIfEmpty(body.GetString());
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (body.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            if (StringProperty(error, "message") is { } nested) return nested;
        }

        if (StringProperty(body, "message") is { } message) return message;

        if (body.TryGetProperty("detail", out var detail))
        {
            if (detail.ValueKind == JsonValueKind.String) return detail.GetString();
            if (StringProperty(detail, "message") is { } nested) return nested;
            if (detail.ValueKind == JsonValueKind.Array) return DescribeValidationErrors(detail);
        }

        return null;
    }

    // FastAPI detail items: "questions.urgency.score.criteria: List should have at least 1 item".
    // Only loc and msg are read; input and ctx can echo the request, so they never reach a message.
    private static string? DescribeValidationErrors(JsonElement errors)
    {
        var parts = new List<string>();
        foreach (var item in errors.EnumerateArray())
        {
            if (StringProperty(item, "msg") is not { } msg)
            {
                continue;
            }

            var location = item.TryGetProperty("loc", out var loc) && loc.ValueKind == JsonValueKind.Array
                ? string.Join(".", loc.EnumerateArray()
                    .Select(segment => segment.ValueKind == JsonValueKind.String ? segment.GetString() : segment.GetRawText())
                    .Where(segment => segment != "body"))
                : string.Empty;
            parts.Add(location.Length > 0 ? location + ": " + msg : msg);
        }

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }

    private static string? StringProperty(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
```

Checked results: the 422 example in T08 gives
`422 questions.urgency.score.criteria: List should have at least 1 item` with the `input` value
absent; `{"unexpected":"..."}` gives `500 status code (no message in body)`; an HTML body is
passed through, capped.

### A.9 RequestWriter

The shape below was checked with a tuple standing in for `Question`; T05 replaces the tuple with
the real question types. The rule it demonstrates is the important part: caller nodes are
written with `WriteTo` and never added to a parent.

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypeSafeSharp;

// Writes caller nodes with WriteTo and never adds them to a new parent: a JsonNode can have one
// parent, and one Question is often shared by concurrent requests (the batch sample does this).
internal static class RequestWriter
{
    public static byte[] Write(JsonNode state, string model, IReadOnlyDictionary<string, (string Type, JsonNode? Instructions, JsonNode? Criteria)> questions, JsonObject? extraBody)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("state");
            state.WriteTo(writer);
            writer.WriteString("model", model);
            writer.WritePropertyName("questions");
            writer.WriteStartObject();
            foreach (var pair in questions)
            {
                writer.WritePropertyName(pair.Key);
                writer.WriteStartObject();
                writer.WriteString("type", pair.Value.Type);
                WriteOpenValue(writer, "instructions", pair.Value.Instructions);
                if (pair.Value.Criteria is not null)
                {
                    WriteOpenValue(writer, "criteria", pair.Value.Criteria);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            if (extraBody is not null)
            {
                foreach (var pair in extraBody)
                {
                    // Collisions with state, model and questions are rejected before writing starts.
                    WriteOpenValue(writer, pair.Key, pair.Value);
                }
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteOpenValue(Utf8JsonWriter writer, string name, JsonNode? value)
    {
        writer.WritePropertyName(name);
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            value.WriteTo(writer);
        }
    }
}
```

In T05, criteria are written directly from the question (an object for Noul and Choice, an
array for Score) instead of a prebuilt `JsonNode`, with the same `WriteOpenValue` for each
value. A `RawQuestion` skips the `WriteStartObject` block: after `WritePropertyName(pair.Key)`
it calls `question.Json.WriteTo(writer)`.

### A.10 ResponseReader

The pattern for required fields, checked with a noul-only reader. T06 extends it to every
answer kind and adds the non-JSON handling.

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace TypeSafeSharp;

// Reads the response with JsonDocument. Each required field has one place that checks it, so a
// malformed body always names its JSON path.
internal static class ResponseReader
{
    public static int LevelKey(string key, string path)
        => int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            ? level
            : throw new TypeSafeResponseValidationException(path + "." + key, "level key is not a non-negative integer");

    private static JsonElement Required(JsonElement parent, string name, JsonValueKind kind, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            throw new TypeSafeResponseValidationException(path + "." + name, "missing");
        }

        return value.ValueKind == kind
            ? value
            : throw new TypeSafeResponseValidationException(path + "." + name, $"expected {kind}, got {value.ValueKind}");
    }

    private static string RequiredString(JsonElement parent, string name, string path)
        => Required(parent, name, JsonValueKind.String, path).GetString()!;
}
```

Parse with `using var document = JsonDocument.Parse(body);` inside `try`/`catch (JsonException)`
(T06 step 2), and `Clone()` every `JsonElement` that outlives the document (`UnknownAnswer.Raw`,
`ScoreAnswer.Legend` values). Probabilities, scores and `noul` use `GetDouble()`; token counts
use `TryGetInt64`. The internal exception constructor `(string jsonPath, string problem)` is the
one from T03 step 5.

### A.11 HttpDefaults

Edited after the check: `AllowAutoRedirect = false` in both branches, and `SetVersion` (2026-10-06).

```csharp
using System;
using System.Net.Http;
using System.Threading;

namespace TypeSafeSharp;

internal static class HttpDefaults
{
    public static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    // The one #if NET in src (ADR-0001): which handler, never what the client does with it.
    // No redirects: following one would turn the POST into a GET and hide the 3xx from the caller.
    public static HttpMessageHandler CreateHandler()
    {
#if NET
        return new SocketsHttpHandler { PooledConnectionLifetime = PooledConnectionLifetime, AllowAutoRedirect = false };
#else
        // .NET Framework allows 2 connections per host by default, which would queue a batch of 4
        // and burn its attempt timeout waiting.
        return new HttpClientHandler { MaxConnectionsPerServer = 64, AllowAutoRedirect = false };
#endif
    }

    // HTTP/2 lets a batch share one connection; OrLower keeps 1.1-only proxies working.
    // .NET Framework's handler throws on 2.0, so ns2.0 leaves the 1.1 default.
    public static void SetVersion(HttpRequestMessage request)
    {
#if NET
        request.Version = System.Net.HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
#endif
    }

#pragma warning disable CA2000 // The HttpClient owns the handler (disposeHandler: true).
    // SDK timers own timeouts, so HttpClient's own 100 s default must not fire first.
    public static HttpClient CreateClient() => new(CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
#pragma warning restore CA2000
}
```

Checked (before the redirect edit): creates and configures without error on `net10.0` and
`net481`.

### A.12 Log

Edited after the check: events 4 and 5 added in the same form as 1 to 3.

```csharp
using Microsoft.Extensions.Logging;

namespace TypeSafeSharp;

// Event ids are part of the contract (ADR-0010). Never add a body, a header or the key as a parameter.
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "{Endpoint} attempt {Attempt} started")]
    public static partial void AttemptStarted(ILogger logger, string endpoint, int attempt);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{Endpoint} {StatusCode} in {ElapsedMs} ms, request {RequestId}")]
    public static partial void AttemptCompleted(ILogger logger, string endpoint, int statusCode, long elapsedMs, string? requestId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Retrying {Endpoint} after {Reason}, retry {Retry} of {MaxRetries}, waiting {DelayMs} ms")]
    public static partial void Retrying(ILogger logger, string endpoint, string reason, int retry, int maxRetries, long delayMs);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Unknown answer kind {Kind} for question {QuestionId}; returned as UnknownAnswer")]
    public static partial void UnknownAnswerKind(ILogger logger, string kind, string questionId);

    // Warning, not Error: the caller decides whether a failed call is an error.
    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "{Endpoint} failed after {Attempts} attempts: {StatusCode}, request {RequestId}")]
    public static partial void CallFailed(ILogger logger, string endpoint, int attempts, string statusCode, string? requestId);
}
```

The `[LoggerMessage]` source generator ships inside `Microsoft.Extensions.Logging.Abstractions`
and works on both targets.

### A.13 Telemetry

Rewritten after the check (the metrics went to P5); not compiled in this form. Build it first
in T12.

```csharp
using System;
using System.Diagnostics;
using System.Globalization;

namespace TypeSafeSharp;

// Tag names follow the OpenTelemetry GenAI conventions as of 2026-09-22
// (github.com/open-telemetry/semantic-conventions-genai). They are marked Development and move;
// note any rename in the changelog (ADR-0010).
internal static class Telemetry
{
    public const string Name = "TypeSafeSharp";

    public static readonly ActivitySource Source = new(Name, SdkInfo.Version);

    public static Activity? StartSystemOne(string model, Uri baseUrl)
    {
        var activity = Source.StartActivity("systemone " + model, ActivityKind.Client);
        if (activity is null) return null;

        activity.SetTag("gen_ai.provider.name", "typesafe");
        activity.SetTag("gen_ai.operation.name", "systemone");
        activity.SetTag("gen_ai.request.model", model);
        activity.SetTag("server.address", baseUrl.Host);
        activity.SetTag("server.port", baseUrl.Port);
        return activity;
    }

    public static void Succeeded(Activity? activity, SystemOneResponse response, int statusCode)
    {
        if (activity is null) return;

        activity.SetTag("gen_ai.response.model", response.Model);
        activity.SetTag("gen_ai.usage.input_tokens", response.Usage.InputTokens);
        activity.SetTag("gen_ai.usage.output_tokens", response.Usage.OutputTokens);
        activity.SetTag("http.response.status_code", statusCode);
        activity.SetTag("typesafe.request_id", response.RequestId);
    }

    public static void Failed(Activity? activity, Exception error)
    {
        if (activity is null) return;

        if (error is TypeSafeApiException api)
        {
            var status = (int)api.StatusCode;
            activity.SetTag("http.response.status_code", status);
            activity.SetTag("typesafe.request_id", api.RequestId);
            activity.SetTag("error.type", status.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            activity.SetTag("error.type", error.GetType().FullName);
        }

        activity.SetStatus(ActivityStatusCode.Error);
    }
}
```

`Succeeded` needs the 2xx status, so the virtual `SystemOneAsync` keeps it from the
`RawResponse`. Never tag state, instructions, answers or headers.

### A.14 Batch loop

The loop as checked, with a generic call standing in for `SystemOneAsync` and a tuple for
`BatchItem`. T13 adds `Index`, `Elapsed` and the fatal-error rethrow in `RunOneAsync`.

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace TypeSafeSharp;

internal static class BatchRunner
{
    // Public entry point is not an iterator, so argument errors throw at the call, not at the
    // first MoveNextAsync.
    public static IAsyncEnumerable<(TItem Item, string? Result, Exception? Error)> Run<TItem>(
        IEnumerable<TItem> items,
        Func<TItem, CancellationToken, Task<string>> call,
        int maxConcurrency,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(items);
        Guard.NotNull(call);
        if (maxConcurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), maxConcurrency, "Must be at least 1.");
        }

        return RunCore(items, call, maxConcurrency, cancellationToken);
    }

    private static async IAsyncEnumerable<(TItem, string?, Exception?)> RunCore<TItem>(
        IEnumerable<TItem> items,
        Func<TItem, CancellationToken, Task<string>> call,
        int maxConcurrency,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = new List<Task<(TItem, string?, Exception?)>>(maxConcurrency);
        using var source = items.GetEnumerator();
        try
        {
            while (true)
            {
                while (running.Count < maxConcurrency && source.MoveNext())
                {
                    running.Add(RunOneAsync(source.Current, call, stop.Token));
                }

                if (running.Count == 0)
                {
                    yield break;
                }

                var finished = await Task.WhenAny(running).ConfigureAwait(false);
                running.Remove(finished);

                // RunOneAsync only throws for cancellation or a fatal error, which must end the enumeration.
                yield return await finished.ConfigureAwait(false);
            }
        }
        finally
        {
            // Reached on completion, on cancellation, and when the caller breaks out early.
            stop.Cancel();
            foreach (var task in running)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected: this is the in-flight work being stopped.
                }
            }
        }
    }

    private static async Task<(TItem, string?, Exception?)> RunOneAsync<TItem>(TItem item, Func<TItem, CancellationToken, Task<string>> call, CancellationToken cancellationToken)
    {
        try
        {
            return (item, await call(item, cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is TypeSafeException or ArgumentException)
        {
            return (item, null, ex);
        }
    }
}
```

Checked result: six items with concurrency 2 and one failing item came back in completion order
with the failure as an item. In T13 the `when` filter excludes the four fatal types so they
propagate. In the `finally` loop, also catch those fatal types from the other in-flight tasks, so
the first fatal error is the one the caller sees.

## Appendix B: fixtures

From `https://docs.typesafe.ai/api.md`, read 2026-09-24. File names are under
`tests/TypeSafeSharp.Tests/Fixtures/`. Copy every value exactly; they are the spec. Tests
compare JSON canonically, so the layout here does not matter.

`requests/noul-basic.json` (the writer emits no `criteria` for a Noul without criteria, so this
compares equal):

```json
{
  "state": "Help! My payouts have been failing for 3 days.",
  "model": "jev-latest",
  "questions": {
    "is_urgent": { "type": "noul", "instructions": "Does this convey urgency?" }
  }
}
```

`requests/noul-criteria.json`

```json
{
  "state": "Help! My payouts have been failing for 3 days.",
  "model": "jev-latest",
  "questions": {
    "is_urgent": {
      "type": "noul",
      "instructions": "Does this convey urgency?",
      "criteria": { "true": "Explicitly time-sensitive", "false": "No urgency expressed" }
    }
  }
}
```

`requests/choice.json`

```json
{
  "state": "Help! My payouts have been failing for 3 days.",
  "model": "jev-latest",
  "questions": {
    "department": {
      "type": "choice",
      "instructions": "Which team should handle this?",
      "criteria": {
        "billing": "Payments, invoicing, refunds",
        "technical": "Bugs, outages, integrations",
        "sales": "Pricing, upgrades, new accounts"
      }
    }
  }
}
```

`requests/score.json`

```json
{
  "state": "Help! My payouts have been failing for 3 days.",
  "model": "jev-latest",
  "questions": {
    "frustration": {
      "type": "score",
      "instructions": "How frustrated is the customer?",
      "criteria": ["Calm", "Frustrated", "Very angry"]
    }
  }
}
```

`responses/noul.json`

```json
{
  "model": "jev-1.13.0",
  "answers": { "is_urgent": { "type": "noul", "noul": 0.95 } },
  "usage": { "input_tokens": 296, "output_tokens": 20 }
}
```

`responses/choice.json`

```json
{
  "model": "jev-1.13.0",
  "answers": {
    "department": {
      "type": "choice",
      "choice": "billing",
      "probabilities": { "billing": 0.88, "technical": 0.12, "sales": 0.0 },
      "confidence": 0.81
    }
  },
  "usage": { "input_tokens": 318, "output_tokens": 34 }
}
```

`responses/score.json`

```json
{
  "model": "jev-1.13.0",
  "answers": {
    "frustration": {
      "type": "score",
      "score": 1.05,
      "legend": { "0": "Calm", "1": "Frustrated", "2": "Very angry" },
      "probabilities": { "0": 0.0, "1": 0.95, "2": 0.05 },
      "confidence": 0.92
    }
  },
  "usage": { "input_tokens": 304, "output_tokens": 18 }
}
```

`responses/models.json` is not in the API reference. Build it from the Models page: one entry
per model with `name`, `description` and `release_date`, and mark it in a comment in the test
as hand-built.

`responses/error-422.json` (hand-built from the OpenAPI `ValidationError` schema):

```json
{
  "detail": [
    {
      "loc": ["body", "questions", "urgency", "score", "criteria"],
      "msg": "List should have at least 1 item",
      "type": "too_short",
      "input": "MARKER_STATE_TEXT",
      "ctx": { "min_length": 1 }
    }
  ]
}
```

Hand-built malformed responses for T06. Each is a 2xx body in the tests:

- `unknown-kind.json`: `noul.json` plus a second answer `"rank": { "type": "ranking", "order": ["a", "b"] }`.
- `malformed-legend.json`: `score.json` with legend key `"one"` in place of `"1"`.
- `malformed-empty.json`: zero bytes.
- `malformed-html.json`: `<html><body>502 Bad Gateway</body></html>`, as a gateway sends it.
- `malformed-array.json`: `[]`.
- `malformed-fractional-tokens.json`: `noul.json` with `"input_tokens": 1.5`.

## Appendix C: retry loop

The algorithm for `Transport.SendAsync`. T09 implements one pass of it, T11 the loop, T12 the
`Log.*` lines. `time` is the `TimeProvider`, `call` the `CallSettings`, `policy` its
`RetrySettings` (T07 step 4 has the constants).

```text
start = time.GetTimestamp()
retry = 0
lastError = null
loop:
    remaining = call.TotalTimeout - time.GetElapsedTime(start)
    if remaining <= 0:
        throw lastError ?? TypeSafeTimeoutException(call.TotalTimeout)
    attemptLimit = min(call.AttemptTimeout, remaining)
    cutByBudget = remaining < call.AttemptTimeout
    if disposed:
        throw ObjectDisposedException(nameof(TypeSafeClient))
    serverDelay = null

    Log.AttemptStarted(endpoint, retry + 1)
    attemptCts = CreateLinkedTokenSource(callerToken)
    timer = Timing.CancelAfter(time, attemptCts, attemptLimit)
    try:
        build the request (T09 step 6), with X-TypeSafe-Retry-Count: retry when retry > 0
        send with attemptCts.Token, read the body bytes
    catch Exception as ex when callerToken.IsCancellationRequested:
        throw new OperationCanceledException(ex.Message, ex, callerToken)
    catch Exception when disposed:                     (HttpClient.Dispose cancels in-flight sends)
        throw ObjectDisposedException(nameof(TypeSafeClient))    (never retried)
    catch OperationCanceledException:                  (our timer, or HttpClient.Timeout)
        error = TypeSafeTimeoutException(cutByBudget ? call.TotalTimeout : attemptLimit)
        retryable = policy.RetryOnTimeout and not cutByBudget
    catch HttpRequestException or IOException as ex:
        error = TypeSafeConnectionException(ex)
        retryable = true
    catch Exception as ex when ex is not TypeSafeException:   (a caller's handler, such as Polly)
        error = TypeSafeConnectionException(ex)
        retryable = false
    finally:
        dispose timer, then attemptCts

    if a response arrived:
        Log.AttemptCompleted(endpoint, status, elapsedMs, requestId)
        if status is 2xx: return RawResponse
        serverDelay = RetryTiming.ParseRetryAfterMilliseconds(response.Headers, time.GetUtcNow())
        error = ErrorMapper.Create(status, body, endpoint, requestId, headers, serverDelay)
        retryable = RetrySettings.IsRetryableStatus(status)
        dispose the response

    if not retryable or retry >= policy.MaxRetries:
        Log.CallFailed(endpoint, retry + 1, reason, requestId)
        throw error
    delay = serverDelay is not null and serverDelay <= RetrySettings.MaxRetryAfter
        ? serverDelay
        : RetryTiming.Backoff(retry, RetrySettings.InitialBackoff, RetrySettings.MaxBackoff, RetrySettings.Jitter, jitter())
    if time.GetElapsedTime(start) + delay >= call.TotalTimeout:
        Log.CallFailed(endpoint, retry + 1, reason, requestId)
        throw error                                     (the budget would be spent waiting)
    Log.Retrying(endpoint, reason, retry + 1, policy.MaxRetries, delay)
    await Timing.DelayAsync(time, delay, callerToken)   (caller cancellation propagates)
    lastError = error
    retry = retry + 1
```

`reason` is the status code as a string, `"timeout"` or `"connection error"`. The error is never
thrown inside the `try` block, so the `catch` clauses see only failures from the handler chain.
The order of the clauses is the rule: caller cancellation wins over everything, then dispose,
then timeout.
