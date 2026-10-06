Unofficial .NET client for [TypeSafe AI](https://docs.typesafe.ai)'s System One API and its Jev
model. It follows the official JS and Python SDKs (retries, errors, key handling) on
`netstandard2.0` and `net10.0`, with typed answers and OpenTelemetry tracing. Not affiliated with
or endorsed by TypeSafe AI.

```csharp
using TypeSafeSharp;

using var client = new TypeSafeClient(new TypeSafeClientOptions());  // reads TYPESAFE_API_KEY

var response = await client.SystemOneAsync(
    "I was charged twice this month. Please fix it today.",
    new Dictionary<string, Question>
    {
        ["category"] = Question.Choice("What is this message about?", "billing", "technical", "other"),
        ["is_urgent"] = Question.Noul("The customer needs action today"),
    });

var category = response.GetChoice("category");
Console.WriteLine($"{category.Choice} ({category.Confidence:P0}), urgent {response.GetNoul("is_urgent").Noul:P0}");
```

Create one client per app and share it. Server-side only: do not ship a key inside an app you
distribute. For ASP.NET Core, add
[TypeSafeSharp.Extensions.DependencyInjection](https://www.nuget.org/packages/TypeSafeSharp.Extensions.DependencyInjection).

- [API reference](https://github.com/sanamhub/typesafe-dotnet/wiki): every public type
- [README](https://github.com/sanamhub/typesafe-dotnet#readme): batches, testing your code,
  resilience pipelines, recipes
- [Limits](https://github.com/sanamhub/typesafe-dotnet#limits): no streaming, async only, no
  metrics yet, retried timeouts can bill twice
- [Changelog](https://github.com/sanamhub/typesafe-dotnet/blob/main/CHANGELOG.md)
- [Security policy](https://github.com/sanamhub/typesafe-dotnet/blob/main/SECURITY.md)
