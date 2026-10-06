// ASP.NET Core minimal API using TypeSafeSharp through dependency injection.
//
// Key: user secrets or TYPESAFE_API_KEY. The README shows how to set either without the key
// landing in shell history. Never put the key in appsettings.json.
//
// Try it:
//   curl -X POST http://localhost:5080/triage -H "Content-Type: application/json" \
//        -d '{"message":"I was charged twice this month. Please fix it today."}'
using TypeSafeSharp;

// Every accepted message is billed to your key, so cap what one caller can send.
const int MaxMessageLength = 4000;

var builder = WebApplication.CreateBuilder(args);

// Binds the "TypeSafe" section and registers TypeSafeClient as a singleton over IHttpClientFactory.
// A missing key stops the app at startup (app.Run), not on the first request.
builder.Services.AddTypeSafe(builder.Configuration.GetSection("TypeSafe"));

var app = builder.Build();

var triage = new Dictionary<string, Question>
{
    ["category"] = Question.Choice("What is this message about?", "billing", "technical", "account", "other"),
    ["is_urgent"] = Question.Noul("The customer needs action today"),
};

// No authentication: this endpoint is for trying the sample on localhost only. Anyone who can
// reach it spends your key.
app.MapPost("/triage", async (TicketRequest ticket, TypeSafeClient client, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(ticket.Message) || ticket.Message.Length > MaxMessageLength)
    {
        return Results.Problem(
            $"message is required and must be at most {MaxMessageLength} characters.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        var response = await client.SystemOneAsync(ticket.Message, triage, cancellationToken);
        var category = response.GetChoice("category");

        return Results.Ok(new TriageResult(
            category.Choice,
            category.Confidence,
            response.GetNoul("is_urgent").Noul,
            response.Model,
            response.RequestId));
    }
    catch (TypeSafeRateLimitException ex)
    {
        // Retries are already spent. Pass the back-pressure on instead of failing hard.
        return Results.Problem(
            "Classification is rate limited, try again shortly.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["retryAfterSeconds"] = ex.RetryAfter?.TotalSeconds });
    }
});

app.Run();

// Nullable because a JSON body without "message" binds to null.
internal sealed record TicketRequest(string? Message);

internal sealed record TriageResult(string Category, double Confidence, double Urgency, string Model, string? RequestId);
