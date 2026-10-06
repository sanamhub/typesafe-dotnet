# ADR-0001: Target netstandard2.0 and net10.0

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06

## Context

The brief asked for `netstandard2.0`, possibly multi targeted with `net10.0`, and for whatever
current practice recommends.

This library is the opposite case to `Ada.Url`, whose ADR-0001 chose `net10.0` only. Ada.Url is
a performance wrapper where `netstandard2.0` would have cost `[LibraryImport]`, spans with real
`ref` fields, and native loading. This SDK is one JSON HTTP call per operation. Nothing on the
hot path needs a modern runtime API, and the value of the package is reach: a .NET Framework 4.8
line-of-business app should be able to call Jev as easily as an ASP.NET Core 10 service.

Facts that bound the choice, checked on 2026-09-24:

| Fact | Source |
| --- | --- |
| .NET 8 (LTS) and .NET 9 (STS) both reach end of support on 2026-11-10 | learn.microsoft.com/dotnet/core/releases-and-support |
| .NET 10 (LTS) is supported to November 2028 | same |
| Microsoft: "DO start with including a `net8.0` target or later", "CONSIDER including a `netstandard2.0` target if you need broad compatibility or .NET Framework support" | learn.microsoft.com/dotnet/standard/library-guidance/cross-platform-targeting |
| Trimming and AOT analysis need `net8.0` or later; `IsAotCompatible` on `netstandard2.0` is NETSDK1210 | learn.microsoft.com/dotnet/core/deploying/native-aot |
| `System.Text.Json` 10.0.12 targets `netstandard2.0` and brings `Microsoft.Bcl.AsyncInterfaces`, `System.Memory`, `System.Threading.Tasks.Extensions` with it | nuget.org nuspec |
| OpenAI 2.14.0: `netstandard2.0;net8.0;net10.0`. Anthropic 12.50.0: `netstandard2.0;net8.0;net9.0`. OllamaSharp 5.4.30: `netstandard2.0;netstandard2.1;net8.0;net9.0;net10.0`. `TypeSafe.AI.Sdk` 0.3.0: `netstandard2.0;net10.0` | nuget.org nuspecs |

## Decision

```xml
<TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
<IsAotCompatible Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">true</IsAotCompatible>
```

1. **`netstandard2.0`** is the reach target. It supports .NET Framework 4.7.2 and later
   (tested on 4.8.1); 4.6.2 may work but is unsupported (its support ends 2027-01-12). It also
   serves .NET 8 and 9 until their end of support on 2026-11-10. TLS defaults are in ADR-0008.
   Mono and Unity may work but are untested.
2. **`net10.0`** is the modern target: no polyfill packages, trim and AOT annotations, and
   `SocketsHttpHandler` connection lifetime (ADR-0005).
3. **No `net8.0` target.** A `net8.0` consumer resolves the `netstandard2.0` asset, which works
   (hand-written JSON reading and writing, no reflection, see ADR-0004). What
   they lose is the AOT annotation, on a runtime that is out of support seven weeks after this
   ADR. Adding `net8.0` now and removing it in November is churn for no user.
4. **Conditional compilation is allowed only as `#if NET`, only for mechanism, never for
   behaviour.** The only expected site is the handler choice in ADR-0005. A CI grep fails on any
   other directive. The same test suite runs on both targets with identical assertions
   (ADR-0012), which is what makes "never for behaviour" checkable.
5. **The rest of the code compiles on both targets without `#if`.** Attributes the
   `netstandard2.0` build lacks, such as `[UnsupportedOSPlatform]` for the browser guard
   (ADR-0008), come from PolySharp. The project keeps
   `<PolySharpIncludeRuntimeSupportedAttributes>true</PolySharpIncludeRuntimeSupportedAttributes>`,
   which is harmless. Argument guards use one internal `Guard` class instead of
   `ArgumentNullException.ThrowIfNull`.

   The other half is the analyzers. With `AnalysisLevel latest-all`, the `net10.0` build suggests
   APIs that `netstandard2.0` lacks (CA1510 to CA1513, CA1845, CA1846, CA1847, CA1849, CA1865 to
   CA1867, CA2249, CA2263). `.editorconfig` sets those rules to none for `src/**` with that
   justification. CA2007 (`ConfigureAwait`) stays an error in `src`, and CA2000 stays on.
6. **Nullable attributes on `netstandard2.0`** come from PolySharp as a private, source-only dev
   dependency, so consumers see no extra package. PolySharp also supplies `init`, `required` and
   records for internal code, but the public API uses none of them. .NET Framework projects
   default to C# 7.3, which cannot call `init` setters or use `with` (PLAN.md section 3).

## Consequences

One package serves .NET Framework, current .NET, and NativeAOT. Both assets carry only small
Microsoft dependencies; the `netstandard2.0` asset adds the packages that `net10.0` has in the
box. PLAN.md section 4.2 is the one list of them.

Two builds and three test legs (`net10.0` on Linux and Windows, `net481` on Windows) on every PR.
That is about three extra CI minutes.

When `net11.0` ships (November 2026, STS), nothing changes: `net10.0` stays the modern target
until `net12.0` (LTS). Revisit then.

## Alternatives considered

**`netstandard2.0` only.** Rejected. It cannot advertise trim or AOT compatibility, and modern
consumers would carry polyfill packages they do not need.

**`netstandard2.0;net8.0;net10.0`**, the OpenAI shape. Rejected for the timing above. If this
ships after 2026-11-10 the question is moot; if it ships before, the `net8.0` asset would live
for weeks.

**`net10.0` only**, the Ada.Url shape. Rejected. It drops every .NET Framework caller, which is
most of the reason to build a .NET SDK for an HTTP API.

**Add `net462`** so .NET Framework avoids the `netstandard` facade assemblies. Rejected for now.
Facade issues were a 4.6.1 to 4.7.1 problem, and 4.6.1 is out of support. The `net481` test leg
will show it if that is wrong.
