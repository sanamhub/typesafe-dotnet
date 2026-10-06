# TypeSafeSharp samples

Four small programs that consume the [TypeSafeSharp](https://github.com/sanamhub/typesafe-dotnet)
package from NuGet, the way an application would, not the source next to them. Use them to try the client against the real API
with your own key.

> **Status:** TypeSafeSharp is not implemented or published yet, so `dotnet restore` fails here.
> The samples are written against the planned API in
> [PLAN.md section 3](https://github.com/sanamhub/typesafe-dotnet/blob/main/docs/PLAN.md) and
> compile with no warnings against a stub of it. The steps in
> [Try an unpublished build](#try-an-unpublished-build) work once the SDK repo has code.

| Project | Shows | Target |
| --- | --- | --- |
| [src/Quickstart](src/Quickstart/Program.cs) | All three question types, mapping a choice onto an enum, structured state, pinned model, models list, error handling | `net10.0` |
| [src/Batch](src/Batch/Program.cs) | `EvaluateManyAsync` re-ranking passages with bounded concurrency; NativeAOT publish | `net10.0` |
| [src/WebApi](src/WebApi/Program.cs) | ASP.NET Core minimal API with `AddTypeSafe`, configuration binding, 429 to 503 | `net10.0` |
| [src/NetFramework](src/NetFramework/Program.cs) | The `netstandard2.0` build from .NET Framework, written in C# 7.3 | `net481`, runs on Windows |

## Set your API key

Get a key from `https://console.typesafe.ai/keys`. Put it in an environment variable or user
secrets. Do not paste it into source files, `appsettings.json`, issues, or chat tools.

PowerShell, current session only (works in Windows PowerShell 5.1 and PowerShell 7):

```powershell
$env:TYPESAFE_API_KEY = [Net.NetworkCredential]::new('', (Read-Host -AsSecureString 'TypeSafe API key')).Password
```

bash or zsh, current session only (the key is not echoed or saved to history):

```bash
read -rs TYPESAFE_API_KEY && export TYPESAFE_API_KEY
```

The web sample can also read it from user secrets, which live outside the repo. Set the
environment variable first as above, then copy it across so the key never appears on a command
line or in history.

bash or zsh:

```bash
printf '{"TypeSafe:ApiKey":"%s"}' "$TYPESAFE_API_KEY" | dotnet user-secrets set --project src/WebApi
```

PowerShell:

```powershell
dotnet user-secrets set "TypeSafe:ApiKey" $env:TYPESAFE_API_KEY --project src/WebApi
```

`.gitignore` excludes `.env` files and `appsettings.Development.json` in case a key ends up in one.

## Run

```bash
dotnet run --project src/Quickstart
```

```bash
dotnet run --project src/Batch
```

```bash
dotnet run --project src/WebApi
```

Then, from a second terminal:

```bash
curl -X POST http://localhost:5080/triage -H "Content-Type: application/json" -d '{"message":"I was charged twice this month. Please fix it today."}'
```

```bash
dotnet run --project src/NetFramework
```

`src/NetFramework` builds on any OS but runs only on Windows with .NET Framework 4.8.1. It is
pinned to C# 7.3, the default for .NET Framework projects, to prove such callers compile.

Each call is billed to your key. The samples send a handful of short, made-up texts, well under a
cent in total. Prices are on TypeSafe's [Models page](https://docs.typesafe.ai/models).

### NativeAOT

```bash
dotnet publish src/Batch -c Release
```

The publish must finish with no IL2xxx or IL3xxx warnings. It needs the platform linker: the
"Desktop development with C++" workload on Windows, or `clang` on Linux.

## Choose the package version

`Directory.Packages.props` pins one version for both packages:

```xml
<TypeSafeSharpVersion>0.1.0</TypeSafeSharpVersion>
```

The first release is a prerelease (`0.1.0-alpha.1`). Change the value to try it.

## Try an unpublished build

1. From this `sample` folder, pack the SDK in the parent folder with a version that nuget.org
   will never have, so the local package cannot be confused with a published one in your NuGet
   cache:

   ```bash
   dotnet pack .. -c Release -o ../artifacts/packages -p:Version=0.1.0-local.1
   ```

2. In `NuGet.Config`, uncomment the `local` source and its `packageSourceMapping` entry.
3. Set `TypeSafeSharpVersion` to `0.1.0-local.1` and run `dotnet restore`.

Undo steps 2 and 3 before going back to nuget.org builds. After repacking, restore still uses the
cached copy of the same version. Delete only the two `0.1.0-local.1` folders,
`~/.nuget/packages/typesafesharp/0.1.0-local.1` and
`~/.nuget/packages/typesafesharp.extensions.dependencyinjection/0.1.0-local.1`, or bump the suffix.
Do not clear the whole cache.

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| `TypeSafeConfigurationException` on start | `TYPESAFE_API_KEY` is missing or empty, or has whitespace inside it, control characters, or non-ASCII characters. Whitespace around the key is stripped. The message names the rule and never shows the key. |
| `OptionsValidationException` when the web sample starts | Same key rules, checked at host start. Set the key in user secrets or `TYPESAFE_API_KEY`. |
| `TypeSafeAuthenticationException` (401) | The API rejected the key. Check it in the console. The exception carries the request id for support. |
| `TypeSafeRateLimitException` (429) | Retries are used up. Lower `BatchOptions.MaxConcurrency`. |
| `NU1101: Unable to find package TypeSafeSharp` | Not published yet, or the version in `Directory.Packages.props` does not exist. |
