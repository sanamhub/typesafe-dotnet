// Re-rank passages for a query: one Score question per passage, run with bounded concurrency,
// results handled as they arrive. This is the pattern from TypeSafe's re-ranking cookbook.
//
// Needs TYPESAFE_API_KEY. Also a NativeAOT check: `dotnet publish -c Release` must finish with
// no trim or AOT warnings.
using TypeSafeSharp;

const string query = "Can a tenant withhold rent if the landlord refuses to fix the heating?";

string[] passages =
[
    "A landlord must keep the premises fit to live in, including heating and hot water.",
    "Rent is due on the first day of each month unless the lease says otherwise.",
    "Some jurisdictions let tenants pay for urgent repairs and deduct the cost from rent, after written notice.",
    "Pets are allowed only with the landlord's written consent.",
    "Withholding rent without following the statutory process can lead to eviction proceedings.",
];

var relevance = Question.Score(
    "How well does the passage help answer the query?",
    "Unrelated",
    "Related topic, does not answer",
    "Partly answers",
    "Directly answers");

using var client = new TypeSafeClient(new TypeSafeClientOptions());
using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(1));

var ranked = new List<(string Passage, double Score, double Confidence)>();

await foreach (var item in client.EvaluateManyAsync(
    passages,
    passage => new SystemOneRequest(
        // Keep the query and the passage apart so instructions can name them.
        new System.Text.Json.Nodes.JsonObject { ["query"] = query, ["passage"] = passage },
        new Dictionary<string, Question> { ["relevance"] = relevance }),
    new BatchOptions { MaxConcurrency = 4 },
    cancel.Token))
{
    if (!item.Succeeded)
    {
        // One failed passage does not sink the batch.
        Console.Error.WriteLine($"skipped passage {item.Index} \"{Truncate(item.Item)}\": {item.Exception!.Message}");
        continue;
    }

    var score = item.Response!.GetScore("relevance");
    ranked.Add((item.Item, score.Score, score.Confidence));
    Console.WriteLine($"done in {item.Elapsed.TotalMilliseconds,5:F0} ms  {score.Score:F2}  {Truncate(item.Item)}");
}

Console.WriteLine();
Console.WriteLine($"Query: {query}");
foreach (var (passage, score, confidence) in ranked.OrderByDescending(r => r.Score))
{
    Console.WriteLine($"  {score:F2} (confidence {confidence:P0})  {passage}");
}

static string Truncate(string text) => text.Length <= 48 ? text : text[..45] + "...";
