`AddTypeSafe` for [TypeSafeSharp](https://www.nuget.org/packages/TypeSafeSharp), the unofficial
.NET client for TypeSafe AI's System One API. It registers a singleton `TypeSafeClient` over a
named `HttpClient`, binds options from configuration (NativeAOT safe), and checks the API key at
host start. Not affiliated with or endorsed by TypeSafe AI.

```csharp
// appsettings.json: { "TypeSafe": { "DefaultModel": "jev-latest" } }. The key comes from
// user secrets, a vault or TYPESAFE_API_KEY, never from appsettings.json.
builder.Services.AddTypeSafe(builder.Configuration.GetSection("TypeSafe"));
```

Then inject `TypeSafeClient`. `AddTypeSafe(o => ...)` configures in code instead. Both return the
`IHttpClientBuilder`, so you can add your own handlers.

- [ASP.NET Core and resilience pipelines](https://github.com/sanamhub/typesafe-dotnet#aspnet-core)
- [Limits](https://github.com/sanamhub/typesafe-dotnet#limits)
- [Changelog](https://github.com/sanamhub/typesafe-dotnet/blob/main/CHANGELOG.md)
