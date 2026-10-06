using System.Text.Json.Nodes;

namespace TypeSafeSharp.Tests;

public sealed class RequestWriterTests
{
    private const string State = "Help! My payouts have been failing for 3 days.";

    private static SystemOneRequest Request(string id, Question question)
        => new(State, new Dictionary<string, Question> { [id] = question });

    [Fact]
    public void NoulBasic_MatchesFixture()
        => Fixture.AssertJsonEqual(
            Fixture.Json("requests/noul-basic.json"),
            RequestWriter.Write(Request("is_urgent", Question.Noul("Does this convey urgency?")), "jev-latest"));

    [Fact]
    public void NoulCriteria_MatchesFixture()
        => Fixture.AssertJsonEqual(
            Fixture.Json("requests/noul-criteria.json"),
            RequestWriter.Write(
                Request("is_urgent", Question.Noul("Does this convey urgency?", "Explicitly time-sensitive", "No urgency expressed")),
                "jev-latest"));

    [Fact]
    public void Choice_MatchesFixture()
    {
        var question = Question.Choice("Which team should handle this?", new Dictionary<string, JsonNode?>
        {
            ["billing"] = "Payments, invoicing, refunds",
            ["technical"] = "Bugs, outages, integrations",
            ["sales"] = "Pricing, upgrades, new accounts",
        });

        Fixture.AssertJsonEqual(Fixture.Json("requests/choice.json"), RequestWriter.Write(Request("department", question), "jev-latest"));
    }

    [Fact]
    public void Score_MatchesFixture()
        => Fixture.AssertJsonEqual(
            Fixture.Json("requests/score.json"),
            RequestWriter.Write(Request("frustration", Question.Score("How frustrated is the customer?", "Calm", "Frustrated", "Very angry")), "jev-latest"));

    [Fact]
    public void Noul_WithOneCriterion_WritesBothKeys()
    {
        var json = JsonNode.Parse(RequestWriter.Write(Request("q", Question.Noul("x", whenTrue: "yes")), "m"))!;

        var criteria = json["questions"]!["q"]!["criteria"]!.AsObject();
        Assert.Equal("yes", criteria["true"]!.GetValue<string>());
        Assert.True(criteria.ContainsKey("false"));
        Assert.Null(criteria["false"]);
    }

    [Fact]
    public void OpenValues_AreWrittenAsGiven()
    {
        var state = new JsonObject { ["ticket"] = new JsonArray(1, "two", null), ["empty"] = null };
        var request = new SystemOneRequest(state, new Dictionary<string, Question> { ["q"] = Question.Noul(null) });

        var json = JsonNode.Parse(RequestWriter.Write(request, "m"))!;

        Assert.True(JsonNode.DeepEquals(state, json["state"]));
        Assert.True(json["questions"]!["q"]!.AsObject().ContainsKey("instructions"));
        Assert.Null(json["questions"]!["q"]!["instructions"]);
    }

    [Fact]
    public void ExtraBody_FieldsAreAddedAfterQuestions()
    {
        var request = Request("q", Question.Noul("x"));
        request.ExtraBody = new JsonObject { ["temperature"] = 0.2, ["tags"] = new JsonArray("a") };

        var json = JsonNode.Parse(RequestWriter.Write(request, "m"))!.AsObject();

        Assert.Equal(["state", "model", "questions", "temperature", "tags"], json.Select(p => p.Key));
        Assert.Equal(0.2, json["temperature"]!.GetValue<double>());
    }

    [Theory]
    [InlineData("state")]
    [InlineData("model")]
    [InlineData("questions")]
    public void ExtraBody_CollidingKey_Throws(string key)
    {
        var request = Request("q", Question.Noul("x"));
        request.ExtraBody = new JsonObject { [key] = 1 };

        var ex = Assert.Throws<ArgumentException>(() => RequestWriter.Write(request, "m"));

        Assert.Contains($"'{key}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromJson_RoundTripsWithoutReparenting()
    {
        var raw = new JsonObject { ["type"] = "ranking", ["instructions"] = "Rank these", ["criteria"] = new JsonArray("a", "b") };
        var question = Question.FromJson(raw);

        var json = JsonNode.Parse(RequestWriter.Write(Request("r", question), "m"))!;

        Assert.True(JsonNode.DeepEquals(raw, json["questions"]!["r"]));
        Assert.Null(raw.Parent);
        Assert.Null(((RawQuestion)question).Json.Parent);
    }

    [Fact]
    public async Task SharedQuestion_ConcurrentWrites_AreIdenticalAndLeaveNodesUntouched()
    {
        // AC-3.11. One Question and one ExtraBody object shared by 8 concurrent writes.
        var question = Question.Choice(new JsonObject { ["text"] = "Which team?" }, new Dictionary<string, JsonNode?>
        {
            ["billing"] = new JsonObject { ["about"] = "money" },
            ["technical"] = null,
        });
        var extra = new JsonObject { ["trace"] = new JsonObject { ["id"] = "t-1" } };
        var expected = Write();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(Write)));

        Assert.All(results, bytes => Assert.Equal(expected, bytes));
        Assert.Null(question.Instructions!.Parent);
        Assert.Null(question.Options["billing"]!.Parent);
        Assert.Equal("""{"trace":{"id":"t-1"}}""", extra.ToJsonString());
        Assert.Null(extra.Parent);

        byte[] Write()
        {
            var request = new SystemOneRequest(new JsonObject { ["n"] = 1 }, new Dictionary<string, Question> { ["team"] = question })
            {
                ExtraBody = extra,
            };
            return RequestWriter.Write(request, "jev-latest");
        }
    }

    [Fact]
    public void Request_RejectsBadQuestionMaps()
    {
        Assert.Equal("state", Assert.Throws<ArgumentNullException>(() => new SystemOneRequest(null!, new Dictionary<string, Question>())).ParamName);
        Assert.Equal("questions", Assert.Throws<ArgumentNullException>(() => new SystemOneRequest("s", null!)).ParamName);
        Assert.Equal("questions", Assert.Throws<ArgumentException>(() => new SystemOneRequest("s", new Dictionary<string, Question>())).ParamName);
        Assert.Equal("questions", Assert.Throws<ArgumentException>(() => new SystemOneRequest("s", new Dictionary<string, Question> { [""] = Question.Noul("x") })).ParamName);
        Assert.Equal("questions", Assert.Throws<ArgumentException>(() => new SystemOneRequest("s", new Dictionary<string, Question> { ["q"] = null! })).ParamName);
    }

    [Fact]
    public void Request_CopiesTheQuestionMap()
    {
        var map = new Dictionary<string, Question> { ["q"] = Question.Noul("x") };
        var request = new SystemOneRequest("s", map);

        map.Add("later", Question.Noul("y"));

        Assert.Single(request.Questions);
    }
}
