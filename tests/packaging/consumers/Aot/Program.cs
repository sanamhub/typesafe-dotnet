using System.Text.Json.Nodes;
using Consumer;
using TypeSafeSharp;

// AC-4.3: the packed package, published with NativeAOT, makes a real HTTP call and reads both
// answer kinds. The key is fake; this program never reads TYPESAFE_API_KEY.
using var server = new StubServer();
using var client = new TypeSafeClient(new TypeSafeClientOptions
{
    ApiKey = "ts_test_0000000000000000",
    BaseUrl = server.BaseUrl,
    DefaultModel = "jev-latest",
});

var response = await client.SystemOneAsync(
    new JsonObject { ["ticket"] = "Help! My payouts have been failing for 3 days." },
    new Dictionary<string, Question>
    {
        ["is_urgent"] = Question.Noul("Does this convey urgency?"),
        ["department"] = Question.Choice("Which team should handle this?", "billing", "technical"),
    });

var failures = new List<string>();
if (response.GetNoul("is_urgent").Noul != 0.95) failures.Add("noul");
if (response.GetChoice("department").Choice != "billing") failures.Add("choice");
if (response.RequestId != StubServer.RequestId) failures.Add("request id");
if (server.LastAuthorization != "Bearer ts_test_0000000000000000") failures.Add("authorization header");

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAIL: " + string.Join(", ", failures));
    return 1;
}

Console.WriteLine("PASS (Aot)");
return 0;
