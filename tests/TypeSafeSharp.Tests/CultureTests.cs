using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace TypeSafeSharp.Tests;

// Turkish breaks case mapping (i/I), German swaps the decimal separator. Nothing the SDK writes
// or reads may depend on either.
public sealed class CultureTests
{
    private static readonly string[] s_requestFixtures = ["noul-basic", "noul-criteria", "choice", "score"];

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Output_DoesNotDependOnCulture(string culture)
    {
        var expected = Snapshot();
        var savedCulture = CultureInfo.CurrentCulture;
        var savedUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            Assert.Equal(expected, Snapshot());
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUiCulture;
        }
    }

    // Everything culture could touch, as one comparable list of strings.
    private static List<string> Snapshot()
    {
        var values = new List<string>();
        foreach (var name in s_requestFixtures)
        {
            values.Add(Convert.ToBase64String(RequestWriter.Write(RequestFor(name), "jev-latest")));
        }

        var noul = ResponseReader.ReadSystemOne(Fixture.Bytes("responses/noul.json"), null);
        var choice = ResponseReader.ReadSystemOne(Fixture.Bytes("responses/choice.json"), null).GetChoice("department");
        var score = ResponseReader.ReadSystemOne(Fixture.Bytes("responses/score.json"), null).GetScore("frustration");
        values.Add(noul.GetNoul("is_urgent").Noul.ToString("R", CultureInfo.InvariantCulture));
        values.Add(noul.Usage.InputTokens.ToString(CultureInfo.InvariantCulture));
        values.Add(choice.Probabilities["billing"].ToString("R", CultureInfo.InvariantCulture));
        values.Add(score.Score.ToString("R", CultureInfo.InvariantCulture));
        values.Add(string.Join(",", score.Legend.Keys));

        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("Retry-After", "1.5");
        var retryAfter = RetryTiming.ParseRetryAfterMilliseconds(response.Headers, new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(1500, retryAfter);
        values.Add(retryAfter!.Value.ToString("R", CultureInfo.InvariantCulture));

        var validation = ErrorMapper.Create(422, Fixture.Bytes("responses/error-422.json"), "POST /v1/systemone", null, new Dictionary<string, IReadOnlyList<string>>(), null);
        Assert.Equal("422 questions.urgency.score.criteria: List should have at least 1 item", validation.Message);
        values.Add(validation.Message);
        return values;
    }

    private static SystemOneRequest RequestFor(string fixture)
    {
        var json = Fixture.Json("requests/" + fixture + ".json");
        var questions = new Dictionary<string, Question>();
        foreach (var pair in json["questions"]!.AsObject())
        {
            questions.Add(pair.Key, Question.FromJson(pair.Value!.AsObject()));
        }

        return new SystemOneRequest(json["state"]!.DeepClone(), questions);
    }
}
