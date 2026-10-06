using System.Text.Json.Nodes;

namespace TypeSafeSharp.Tests.Live;

// Calls the real API: about ten requests of a few hundred tokens, synthetic text only. CI runs
// these weekly from live.yml with the CI key; pull requests never do (ADR-0012). Answers vary by
// model version, so the assertions check shape and ranges, not exact values.
[Trait("Category", "Live")]
public sealed class LiveTests : IDisposable
{
    private static readonly string[] s_departments = ["billing", "technical", "sales"];

    private const string Ticket = "Help! My payouts have been failing for 3 days and my rent is due tomorrow.";

    private readonly TypeSafeClient _client;

    public LiveTests()
    {
        // Fail, not skip: a live run without a key must not look green.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")))
        {
            Assert.Fail("TYPESAFE_API_KEY is missing. The live tests call the real API and need a key in the environment.");
        }

        _client = new TypeSafeClient(new TypeSafeClientOptions { BaseUrl = new Uri("https://api.typesafe.ai") });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _client?.Dispose();

    [Fact]
    public async Task Noul()
    {
        var response = await _client.SystemOneAsync(Ticket, new Dictionary<string, Question> { ["is_urgent"] = Question.Noul("Does this convey urgency?") }, Ct);

        Assert.InRange(response.GetNoul("is_urgent").Noul, 0, 1);
        Assert.StartsWith("jev-", response.Model, StringComparison.Ordinal);
        Assert.True(response.Usage.InputTokens > 0);
        Assert.False(string.IsNullOrEmpty(response.RequestId));
    }

    [Fact]
    public async Task Choice()
    {
        var question = Question.Choice("Which team should handle this?", new Dictionary<string, JsonNode?>
        {
            ["billing"] = "Payments, invoicing, refunds",
            ["technical"] = "Bugs, outages, integrations",
            ["sales"] = "Pricing, upgrades, new accounts",
        });

        var answer = (await _client.SystemOneAsync(Ticket, new Dictionary<string, Question> { ["department"] = question }, Ct)).GetChoice("department");

        Assert.Contains(answer.Choice, s_departments);
        Assert.InRange(answer.Confidence, 0, 1);
        Assert.Equal(3, answer.Probabilities.Count);
    }

    [Fact]
    public async Task Score()
    {
        var answer = (await _client.SystemOneAsync(
            Ticket,
            new Dictionary<string, Question> { ["frustration"] = Question.Score("How frustrated is the customer?", "Calm", "Frustrated", "Very angry") },
            Ct)).GetScore("frustration");

        Assert.InRange(answer.Score, 0, 2);
        Assert.InRange(answer.MostLikelyLevel, 0, 2);
        Assert.Equal(3, answer.Legend.Count);
    }

    [Fact]
    public async Task Models()
    {
        var models = await _client.Models.ListAsync(Ct);

        Assert.NotEmpty(models);
        Assert.All(models, m => Assert.False(string.IsNullOrEmpty(m.Name)));
    }

    [Fact]
    public async Task StructuredState()
    {
        var state = new JsonObject
        {
            ["subject"] = "Refund request",
            ["body"] = "I was charged twice for order 1042.",
            ["previous_tickets"] = new JsonArray("Login issue, resolved"),
        };

        var response = await _client.SystemOneAsync(state, new Dictionary<string, Question> { ["about_billing"] = Question.Noul("Is this about billing?") }, Ct);

        Assert.InRange(response.GetNoul("about_billing").Noul, 0, 1);
    }

    [Fact]
    public async Task PinnedModel()
    {
        var models = await _client.Models.ListAsync(Ct);
        // Prefer a versioned name. The response reports the concrete model that answered, so an
        // alias such as jev-latest comes back as jev-1.13.0 and never echoes the alias
        // (openapi.snapshot.json, SystemOneResponse.model).
        var pinned = models.Select(m => m.Name).FirstOrDefault(IsVersionedModel) ?? models[0].Name;
        var request = new SystemOneRequest(Ticket, new Dictionary<string, Question> { ["is_urgent"] = Question.Noul("Does this convey urgency?") }) { Model = pinned };

        var response = await _client.SystemOneAsync(request, Ct);

        if (IsVersionedModel(pinned))
        {
            Assert.Equal(pinned, response.Model);
        }
        else
        {
            // Every listed name is an alias, so the response cannot echo it. It is still a jev-
            // name, and a model field that never reaches the API fails UnknownModel below with a
            // 4xx instead of a 2xx.
            Assert.StartsWith("jev-", response.Model, StringComparison.Ordinal);
        }
    }

    private static bool IsVersionedModel(string name) =>
        name.StartsWith("jev-", StringComparison.Ordinal)
        && name.Length > "jev-".Length
        && char.IsDigit(name["jev-".Length]);

    [Fact]
    public async Task UnknownModel_IsAClientErrorWithRequestId()
    {
        var request = new SystemOneRequest(Ticket, new Dictionary<string, Question> { ["is_urgent"] = Question.Noul("Does this convey urgency?") })
        {
            Model = "jev-does-not-exist",
        };

        var ex = await Assert.ThrowsAnyAsync<TypeSafeApiException>(() => _client.SystemOneAsync(request, Ct));

        Assert.InRange((int)ex.StatusCode, 400, 499);
        Assert.False(string.IsNullOrEmpty(ex.RequestId));
    }

    [Fact]
    public async Task BatchOfThree()
    {
        string[] tickets = ["My card was charged twice.", "The export button does nothing.", "Do you offer a yearly plan?"];
        var question = Question.Noul("The customer needs action today");

        var results = new List<BatchItem<string>>();
        await foreach (var item in _client.EvaluateManyAsync(
            tickets,
            ticket => new SystemOneRequest(ticket, new Dictionary<string, Question> { ["is_urgent"] = question }),
            Ct))
        {
            results.Add(item);
        }

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Succeeded, r.Exception?.Message));
    }
}
