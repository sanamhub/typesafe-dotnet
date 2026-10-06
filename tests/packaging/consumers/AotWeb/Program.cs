using TypeSafeSharp;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddTypeSafe(builder.Configuration.GetSection("TypeSafe"));

var app = builder.Build();
app.MapPost("/urgent", async (TypeSafeClient client, HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var text = await reader.ReadToEndAsync();
    var response = await client.SystemOneAsync(text, new Dictionary<string, Question> { ["is_urgent"] = Question.Noul("Is this urgent?") });
    return Results.Text(response.GetNoul("is_urgent").Noul.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
});

app.Run();
