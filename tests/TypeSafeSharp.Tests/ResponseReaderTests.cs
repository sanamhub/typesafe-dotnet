using System.Text;
using System.Text.Json;

namespace TypeSafeSharp.Tests;

public sealed class ResponseReaderTests
{
    private static SystemOneResponse Read(string fixture, string? requestId = null)
        => ResponseReader.ReadSystemOne(Fixture.Bytes("responses/" + fixture), requestId);

    private static string PathOf(byte[] body)
        => Assert.Throws<TypeSafeResponseValidationException>(() => ResponseReader.ReadSystemOne(body, null)).JsonPath!;

    private static string PathOf(string json) => PathOf(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Noul_MatchesFixture()
    {
        var response = Read("noul.json", "req_1");

        Assert.Equal("jev-1.13.0", response.Model);
        Assert.Equal(0.95, response.GetNoul("is_urgent").Noul);
        Assert.Equal(296, response.Usage.InputTokens);
        Assert.Equal(20, response.Usage.OutputTokens);
        Assert.Equal("req_1", response.RequestId);
    }

    [Fact]
    public void Choice_MatchesFixture()
    {
        var answer = Read("choice.json").GetChoice("department");

        Assert.Equal("billing", answer.Choice);
        Assert.Equal(0.81, answer.Confidence);
        Assert.Equal(0.88, answer.Probabilities["billing"]);
        Assert.Equal(0.12, answer.Probabilities["technical"]);
        Assert.Equal(0.0, answer.Probabilities["sales"]);
    }

    [Fact]
    public void Score_MatchesFixture()
    {
        var answer = Read("score.json").GetScore("frustration");

        Assert.Equal(1.05, answer.Score);
        Assert.Equal(0.92, answer.Confidence);
        Assert.Equal("Frustrated", answer.Legend[1].GetString());
        Assert.Equal(0.95, answer.Probabilities[1]);
        Assert.Equal(1, answer.MostLikelyLevel);
    }

    [Fact]
    public void Score_LegendOutlivesTheDocument()
    {
        var answer = Read("score.json").GetScore("frustration");
        GC.Collect();

        Assert.Equal("Very angry", answer.Legend[2].GetString());
    }

    [Fact]
    public void Score_Tie_PicksLowerLevel()
        => Assert.Equal(1, TypeSafeModelFactory.ScoreAnswer(1.5, 0.5, new Dictionary<int, JsonElement>(), new Dictionary<int, double> { [2] = 0.5, [1] = 0.5 }).MostLikelyLevel);

    [Fact]
    public void TypeLast_StillParses()
        => Assert.Equal(0.4, ResponseReader.ReadSystemOne(
            Encoding.UTF8.GetBytes("""{"model":"m","answers":{"q":{"noul":0.4,"type":"noul"}},"usage":{"input_tokens":1,"output_tokens":2}}"""),
            null).GetNoul("q").Noul);

    [Fact]
    public void UnknownKind_IsKeptRawAndTheRestParses()
    {
        var response = Read("unknown-kind.json");

        var unknown = Assert.IsType<UnknownAnswer>(response.Answers["rank"]);
        Assert.Equal("ranking", unknown.Type);
        Assert.Equal("b", unknown.Raw.GetProperty("order")[1].GetString());
        Assert.Equal(0.95, response.GetNoul("is_urgent").Noul);
        Assert.Equal(2, response.Answers.Count);
    }

    [Fact]
    public void Models_AreRead()
    {
        // models.json is hand-built: the API reference shows no response for GET /v1/models.
        var model = Assert.Single(ResponseReader.ReadModels(Fixture.Bytes("responses/models.json")));

        Assert.Equal("jev-1.13.0", model.Name);
        Assert.Equal("Jev 1.13", model.Description);
        Assert.Equal("2026-09-01", model.ReleaseDate);
    }

    [Theory]
    [InlineData("malformed-empty.json")]
    [InlineData("malformed-html.json")]
    [InlineData("malformed-array.json")]
    public void NotAnObject_GivesRootPath(string fixture)
        => Assert.Equal("$", PathOf(Fixture.Bytes("responses/" + fixture)));

    [Fact]
    public void Models_NotJson_GivesRootPath()
        => Assert.Equal("$", Assert.Throws<TypeSafeResponseValidationException>(() => ResponseReader.ReadModels(Fixture.Bytes("responses/malformed-html.json"))).JsonPath);

    [Fact]
    public void FractionalTokens_GivePath()
        => Assert.Equal("$.usage.input_tokens", PathOf(Fixture.Bytes("responses/malformed-fractional-tokens.json")));

    [Fact]
    public void OversizedTokens_GivePath()
        => Assert.Equal("$.usage.output_tokens", PathOf("""{"model":"m","answers":{},"usage":{"input_tokens":1,"output_tokens":99999999999999999999}}"""));

    [Fact]
    public void MalformedLegend_GivesPath()
        => Assert.Equal("$.answers.frustration.legend.one", PathOf(Fixture.Bytes("responses/malformed-legend.json")));

    [Fact]
    public void EmptyScoreProbabilities_GivePath()
        => Assert.Equal(
            "$.answers.s.probabilities",
            PathOf("""{"model":"m","answers":{"s":{"type":"score","score":1,"confidence":1,"legend":{},"probabilities":{}}},"usage":{"input_tokens":1,"output_tokens":1}}"""));

    public static TheoryData<string, string> MissingFields => new()
    {
        { """{"answers":{},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.model" },
        { """{"model":"m","usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers" },
        { """{"model":"m","answers":{}}""", "$.usage" },
        { """{"model":"m","answers":{},"usage":{"output_tokens":1}}""", "$.usage.input_tokens" },
        { """{"model":"m","answers":{},"usage":{"input_tokens":1}}""", "$.usage.output_tokens" },
        { """{"model":5,"answers":{},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.model" },
        { """{"model":"m","answers":[],"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers" },
        { """{"model":"m","answers":{"q":{"noul":1}},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers.q.type" },
        { """{"model":"m","answers":{"q":{"type":"noul"}},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers.q.noul" },
        { """{"model":"m","answers":{"q":{"type":"noul","noul":"high"}},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers.q.noul" },
        { """{"model":"m","answers":{"q":{"type":"choice","confidence":1,"probabilities":{}}},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers.q.choice" },
        { """{"model":"m","answers":{"q":{"type":"choice","choice":"a","probabilities":{}}},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers.q.confidence" },
        { """{"model":"m","answers":{"q":{"type":"choice","choice":"a","confidence":1}},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers.q.probabilities" },
        { """{"model":"m","answers":{"q":{"type":"choice","choice":"a","confidence":1,"probabilities":{"a":"x"}}},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers.q.probabilities.a" },
        { """{"model":"m","answers":{"q":{"type":"score","confidence":1,"legend":{},"probabilities":{"0":1}}},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers.q.score" },
        { """{"model":"m","answers":{"q":{"type":"score","score":1,"confidence":1,"probabilities":{"0":1}}},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers.q.legend" },
        { """{"model":"m","answers":{"q":7},"usage":{"input_tokens":1,"output_tokens":1}}""", "$.answers.q" },
    };

    [Theory]
    [MemberData(nameof(MissingFields))]
    public void MissingOrWrongField_GivesPath(string json, string path)
        => Assert.Equal(path, PathOf(json));

    [Fact]
    public void GetNoul_MissingId_ListsAnsweredIds()
    {
        var ex = Assert.Throws<KeyNotFoundException>(() => Read("unknown-kind.json").GetNoul("nope"));

        Assert.Contains("'nope'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'is_urgent', 'rank'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetChoice_OnNoul_NamesActualKind()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Read("noul.json").GetChoice("is_urgent"));

        Assert.Equal("'is_urgent' was answered as noul, not choice.", ex.Message);
    }

    [Fact]
    public void GetScore_OnUnknown_NamesActualKind()
        => Assert.Contains("ranking", Assert.Throws<InvalidOperationException>(() => Read("unknown-kind.json").GetScore("rank")).Message, StringComparison.Ordinal);

    [Fact]
    public void Factory_BuildsResponsesForCallerTests()
    {
        var response = TypeSafeModelFactory.SystemOneResponse(
            "jev-1.13.0",
            new Dictionary<string, Answer> { ["q"] = TypeSafeModelFactory.NoulAnswer(0.7) },
            TypeSafeModelFactory.Usage(10, 2),
            "req_9");

        Assert.Equal(0.7, response.GetNoul("q").Noul);
        Assert.Equal("req_9", response.RequestId);
        Assert.Equal(10, response.Usage.InputTokens);
    }

    [Fact]
    public void Factory_ScoreWithoutProbabilities_Throws()
        => Assert.Equal("probabilities", Assert.Throws<ArgumentException>(
            () => TypeSafeModelFactory.ScoreAnswer(1, 1, new Dictionary<int, JsonElement>(), new Dictionary<int, double>())).ParamName);
}
