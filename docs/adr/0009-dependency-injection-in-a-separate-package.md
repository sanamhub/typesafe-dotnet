# ADR-0009: Dependency injection in a separate package

- **Status:** accepted
- **Date:** 2026-09-24
- **Approved by:** Sanam, 2026-10-06
- **Follows:** ADR-0003 (flat layout)

## Context

ASP.NET Core and Worker Service apps expect `services.AddX(...)`, options bound from
`IConfiguration`, and an `HttpClient` from `IHttpClientFactory`. That needs
`Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions`, and their
transitive graph.

Console apps, Azure Functions isolated workers that construct clients by hand, Unity (untested),
and .NET Framework apps do not want that graph. `TypeSafe.AI.Sdk` puts `Microsoft.Extensions.Http`
in its core package, so every consumer gets it.

## Decision

Two packages, versioned together:

| Package | Depends on | Contents |
| --- | --- | --- |
| `TypeSafeSharp` | See PLAN.md section 4.2 (the one list per target) | Everything in ADR-0003 |
| `TypeSafeSharp.Extensions.DependencyInjection` | `TypeSafeSharp`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions` | `AddTypeSafe` |

The extension surface is two overloads, both returning `IHttpClientBuilder`:

```csharp
// appsettings.json: { "TypeSafe": { "DefaultModel": "jev-latest", "AttemptTimeout": "00:00:10" } }
// API key from user-secrets, Key Vault, or TYPESAFE_API_KEY. Never from appsettings.json.
builder.Services.AddTypeSafe(builder.Configuration.GetSection("TypeSafe"));

builder.Services.AddTypeSafe(o => o.DefaultModel = "jev-1.13.0");
```

A keyed `AddTypeSafe(serviceKey, ...)` is deferred to P5. Until then a caller who needs a second
client (a pinned model, a gateway) registers it with `AddKeyedSingleton`.

Rules:

1. The client is registered as a **singleton**, built over a named `IHttpClientFactory` client
   (`"TypeSafeSharp"`). A singleton over a factory `HttpClient` never sees handler rotation
   (Microsoft Learn,
   [Avoid typed clients in singleton services](https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory#avoid-typed-clients-in-singleton-services)),
   so the package does not rely on it. The named client is configured with
   `ConfigurePrimaryHttpMessageHandler(HttpDefaults.CreateHandler)` (the handler in ADR-0005
   decision 7), `SetHandlerLifetime(Timeout.InfiniteTimeSpan)`, and
   `HttpClient.Timeout = Timeout.InfiniteTimeSpan`.
2. Options are validated with a delegate (`Validate(o => ...)`) that checks
   `ApiKey ?? TYPESAFE_API_KEY` with the core key rules (ADR-0008), so a host that sets only the
   environment variable passes. `ValidateOnStart()` runs it at host start (`StartAsync`, which
   `app.Run()` calls), not at `Build()`. A missing key stops the app at startup, not on the first
   request.
3. The returned `IHttpClientBuilder` lets callers add their own resilience pipeline. The SDK
   must not cut that pipeline off, so the recipe turns off SDK retries and sets both SDK
   timeouts longer than the pipeline's total. The README shows it; it needs
   `Microsoft.Extensions.Http.Resilience`:

   ```csharp
   builder.Services
       .AddTypeSafe(o =>
       {
           o.Retry = RetryPolicy.None;
           o.AttemptTimeout = TimeSpan.FromSeconds(60);   // longer than the pipeline's total
           o.TotalTimeout = TimeSpan.FromSeconds(60);
       })
       .AddStandardResilienceHandler();
   ```
4. The `Retry` section binds `MaxRetries` and `RetryOnTimeout` into the options instance's own
   `RetryPolicy`. Each options object gets a fresh instance, because `RetryPolicy.Default`
   returns a new one on each read.
5. Both packages target `netstandard2.0;net10.0`. The DI package sets `IsAotCompatible` on
   `net10.0` and `EnableConfigurationBindingGenerator=true`. A probe showed that without the
   generator, `OptionsBuilder.Bind` fails the `net10.0` build with IL2026 and IL3050; with it,
   binding is AOT clean on both targets.

## Consequences

The core package stays small for everyone. DI users add one package and one line.

Two packages means two IDs to reserve, two READMEs, and a CI check that both carry the same
version (PLAN.md section 6).

## Alternatives considered

**DI extensions inside the core package.** Rejected for the dependency graph above.

**Register as transient, as `TypeSafe.AI.Sdk` does.** Rejected. The client holds settings and a
logger, both immutable, so a singleton is correct and avoids per-request allocation.
