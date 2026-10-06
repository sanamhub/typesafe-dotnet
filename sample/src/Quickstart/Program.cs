// Quickstart for TypeSafeSharp: one request with all three question types, typed answers,
// structured state, a pinned model, the models list, and error handling.
//
// Needs TYPESAFE_API_KEY in the environment. The text below is made up; do not paste real
// customer data into a sample.
using System.Text.Json.Nodes;
using TypeSafeSharp;

try
{
    // Built inside try because a bad or missing key throws here, before any request.
    // A null ApiKey means "read TYPESAFE_API_KEY". TYPESAFE_BASE_URL and TYPESAFE_DEFAULT_MODEL are
    // honoured too.
    using var client = new TypeSafeClient(new TypeSafeClientOptions());

    await ListModelsAsync(client);
    await TriageTicketAsync(client);
    await CompareRecordsAsync(client);
}
catch (TypeSafeConfigurationException ex)
{
    // Bad or missing key. The message says which rule failed and never echoes the key.
    Console.Error.WriteLine($"Configuration: {ex.Message}");
    return 1;
}
catch (TypeSafeAuthenticationException ex)
{
    Console.Error.WriteLine($"The API rejected the key (401). Request id: {ex.RequestId}");
    return 1;
}
catch (TypeSafeRateLimitException ex)
{
    // Only reached after the built-in retries are used up.
    Console.Error.WriteLine($"Rate limited. Server asked to wait {ex.RetryAfter?.TotalSeconds ?? 0} s.");
    return 1;
}
catch (TypeSafeApiException ex)
{
    Console.Error.WriteLine($"{ex.Endpoint} failed with {ex.StatusCode}: {ex.Message} (request {ex.RequestId})");
    return 1;
}

return 0;

static async Task ListModelsAsync(TypeSafeClient client)
{
    Console.WriteLine("Models available to this key:");
    foreach (var model in await client.Models.ListAsync())
    {
        Console.WriteLine($"  {model.Name,-12} {model.ReleaseDate}  {model.Description}");
    }

    Console.WriteLine();
}

static async Task TriageTicketAsync(TypeSafeClient client)
{
    const string ticket =
        "Hi, I've been trying to connect my Stripe account for 3 days and the integration keeps " +
        "failing. I'm losing sales. Please help ASAP.";

    // All questions go in one request. Jev reads the state once and answers them in parallel,
    // so three questions cost about the same as one.
    var response = await client.SystemOneAsync(ticket, new Dictionary<string, Question>
    {
        ["department"] = Question.Choice(
            "Which team should handle this?",
            new Dictionary<string, JsonNode?>
            {
                ["billing"] = "Payment or subscription issues",
                ["technical"] = "Bugs or integration problems",
                ["sales"] = "Pricing or account questions",
            }),
        ["frustration"] = Question.Score(
            "How frustrated does the customer appear?",
            "Calm, just stating facts",
            "Frustrated but civil",
            "Very angry, strong language"),
        ["is_urgent"] = Question.Noul("The message conveys urgency or time-sensitivity"),
    });

    var department = response.GetChoice("department");
    // The answer is one of the option keys above, so it maps straight onto your own enum.
    var team = Enum.Parse<Department>(department.Choice, ignoreCase: true);
    var frustration = response.GetScore("frustration");
    var urgent = response.GetNoul("is_urgent");

    Console.WriteLine($"Answered by {response.Model}, {response.Usage.InputTokens} input tokens");
    Console.WriteLine($"  department  {department.Choice} (confidence {department.Confidence:P0})");
    Console.WriteLine($"  frustration {frustration.Score:F2} on a 0 to 2 scale, most likely level {frustration.MostLikelyLevel}");
    Console.WriteLine($"  urgent      {urgent.Noul:P0}");

    // Confidence is the second axis: the answer says what, confidence says whether to act.
    // The thresholds are yours to set. These are an example, not a recommendation.
    var action = department.Confidence switch
    {
        >= 0.8 => $"route to {team}",
        >= 0.5 => $"suggest {team}, ask an agent to confirm",
        _ => "send to the general queue for a human",
    };
    Console.WriteLine($"  action      {action}");
    Console.WriteLine();
}

static async Task CompareRecordsAsync(TypeSafeClient client)
{
    // State can be structured. Refer to fields by name in backticks from the instructions.
    var state = new JsonObject
    {
        ["candidate"] = new JsonObject
        {
            ["name"] = "Jordan Lee",
            ["location"] = "Oakland, California",
            ["last_employer"] = "Northwind Traders",
        },
        ["existing_record"] = new JsonObject
        {
            ["name"] = "J. Lee",
            ["location"] = "Oakland, CA",
            ["last_employer"] = "Northwind",
        },
    };

    var response = await client.SystemOneAsync(new SystemOneRequest(state, new Dictionary<string, Question>
    {
        ["same_person"] = Question.Noul(
            "Is `candidate` the same person as `existing_record`?",
            whenTrue: "Name, place and employer are consistent with one person",
            whenFalse: "At least one field clearly points to a different person"),
    })
    {
        // Pin a version when you have tuned thresholds against it; aliases move.
        Model = "jev-1.13.0",
    });

    Console.WriteLine($"Same person: {response.GetNoul("same_person").Noul:P0} (request {response.RequestId})");
}

internal enum Department
{
    Billing,
    Technical,
    Sales,
}
