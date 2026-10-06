using System.Net.Http;
using System.Text;

namespace TypeSafeSharp.Tests;

public sealed class ErrorMappingTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> s_noHeaders = new(StringComparer.OrdinalIgnoreCase);

    internal static TypeSafeApiException Map(int status, string body, double? retryAfterMs = null)
        => ErrorMapper.Create(status, Encoding.UTF8.GetBytes(body), "POST /v1/systemone", "req_1", s_noHeaders, retryAfterMs);

    [Theory]
    [InlineData(400, typeof(TypeSafeBadRequestException))]
    [InlineData(401, typeof(TypeSafeAuthenticationException))]
    [InlineData(403, typeof(TypeSafePermissionDeniedException))]
    [InlineData(404, typeof(TypeSafeNotFoundException))]
    [InlineData(422, typeof(TypeSafeUnprocessableEntityException))]
    [InlineData(429, typeof(TypeSafeRateLimitException))]
    [InlineData(500, typeof(TypeSafeInternalServerException))]
    [InlineData(529, typeof(TypeSafeOverloadedException))]
    [InlineData(302, typeof(TypeSafeApiException))]
    [InlineData(418, typeof(TypeSafeApiException))]
    public void Status_GivesType(int status, Type expected)
    {
        var ex = Map(status, """{"error":"nope"}""");

        Assert.Equal(expected, ex.GetType());
        Assert.Equal("req_1", ex.RequestId);
        Assert.Equal("POST /v1/systemone", ex.Endpoint);
        Assert.Equal(status + " nope", ex.Message);
    }

    [Fact]
    public void Validation422_FlattensDetail()
    {
        var ex = Assert.IsType<TypeSafeUnprocessableEntityException>(
            ErrorMapper.Create(422, Fixture.Bytes("responses/error-422.json"), "POST /v1/systemone", null, s_noHeaders, null));

        Assert.Equal("422 questions.urgency.score.criteria: List should have at least 1 item", ex.Message);
        var error = Assert.Single(ex.ValidationErrors);
        Assert.Equal(["questions", "urgency", "score", "criteria"], error.Location);
        Assert.Equal("List should have at least 1 item", error.Message);
        Assert.Equal("too_short", error.Type);
    }

    [Fact]
    public void Validation422_NeverCarriesInput()
    {
        // AC-3.12. The fixture's detail[0].input is the marker, standing in for customer state.
        var ex = Assert.IsType<TypeSafeUnprocessableEntityException>(
            ErrorMapper.Create(422, Fixture.Bytes("responses/error-422.json"), "POST /v1/systemone", null, s_noHeaders, null));

        Assert.DoesNotContain("MARKER_STATE_TEXT", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("MARKER_STATE_TEXT", ex.ToString(), StringComparison.Ordinal);
        Assert.All(ex.ValidationErrors, e =>
        {
            Assert.DoesNotContain("MARKER_STATE_TEXT", e.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("MARKER_STATE_TEXT", e.Type, StringComparison.Ordinal);
            Assert.DoesNotContain("MARKER_STATE_TEXT", string.Join(".", e.Location), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Validation422_NumericLocSegments_UseRawText()
    {
        var ex = (TypeSafeUnprocessableEntityException)Map(422, """{"detail":[{"loc":["body","questions",0],"msg":"bad","type":"t"}]}""");

        Assert.Equal("422 questions.0: bad", ex.Message);
        Assert.Equal(["questions", "0"], ex.ValidationErrors[0].Location);
    }

    [Fact]
    public void Validation422_SeveralItems_JoinWithSemicolon()
        => Assert.Equal(
            "422 a: one; b: two",
            Map(422, """{"detail":[{"loc":["body","a"],"msg":"one"},{"loc":["b"],"msg":"two"}]}""").Message);

    [Theory]
    [InlineData("""{"message":"top"}""", "top")]
    [InlineData("""{"error":{"message":"nested"}}""", "nested")]
    [InlineData("""{"detail":"plain detail"}""", "plain detail")]
    [InlineData("""{"detail":{"message":"nested detail"}}""", "nested detail")]
    [InlineData("\"just a string\"", "just a string")]
    public void MessageFields_AreRead(string body, string expected)
        => Assert.Equal("500 " + expected, Map(500, body).Message);

    [Fact]
    public void JsonWithoutMessage_NeverEchoesTheBody()
    {
        var ex = Map(500, """{"unexpected":"MARKER_STATE_TEXT"}""");

        Assert.Equal("500 status code (no message in body)", ex.Message);
        Assert.Equal("""{"unexpected":"MARKER_STATE_TEXT"}""", ex.Body);
    }

    [Fact]
    public void EmptyBody_SaysSo()
    {
        var ex = ErrorMapper.Create(503, [], "POST /v1/systemone", null, s_noHeaders, null);

        Assert.Equal("503 status code (no body)", ex.Message);
        Assert.Null(ex.Body);
    }

    [Fact]
    public void PlainText_IsCappedAt200()
    {
        var ex = Map(502, new string('x', 300));

        Assert.Equal("502 " + new string('x', 200) + "...", ex.Message);
    }

    [Fact]
    public void RateLimit_CarriesRetryAfter()
        => Assert.Equal(TimeSpan.FromMilliseconds(1500), ((TypeSafeRateLimitException)Map(429, "{}", 1500)).RetryAfter);

    [Fact]
    public void RateLimit_HugeRetryAfter_DoesNotOverflow()
        => Assert.Equal(TimeSpan.MaxValue, ((TypeSafeRateLimitException)Map(429, "{}", 1e300)).RetryAfter);

    [Fact]
    public void RateLimit_WithoutHeader_HasNullRetryAfter()
        => Assert.Null(((TypeSafeRateLimitException)Map(429, "{}")).RetryAfter);

    private static double? RetryAfter(params (string Name, string Value)[] headers)
    {
        using var response = new HttpResponseMessage();
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return RetryTiming.ParseRetryAfterMilliseconds(response.Headers, new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void RetryAfter_Seconds() => Assert.Equal(2000, RetryAfter(("Retry-After", "2")));

    [Fact]
    public void RetryAfter_FractionalSeconds() => Assert.Equal(1500, RetryAfter(("Retry-After", "1.5")));

    [Fact]
    public void RetryAfterMs_WinsOverRetryAfter() => Assert.Equal(250, RetryAfter(("retry-after-ms", "250"), ("Retry-After", "9")));

    [Fact]
    public void RetryAfter_HttpDate() => Assert.Equal(3000, RetryAfter(("Retry-After", "Tue, 06 Oct 2026 12:00:03 GMT")));

    [Fact]
    public void RetryAfter_PastDate_IsZero() => Assert.Equal(0, RetryAfter(("Retry-After", "Tue, 06 Oct 2026 11:00:00 GMT")));

    [Theory]
    [InlineData("-1")]
    [InlineData("soon")]
    [InlineData("")]
    public void RetryAfter_Invalid_IsNull(string value) => Assert.Null(RetryAfter(("Retry-After", value)));

    [Fact]
    public void RetryAfter_Absent_IsNull() => Assert.Null(RetryAfter());

    [Fact]
    public void RetryAfterMs_Invalid_FallsBackToRetryAfter() => Assert.Equal(4000, RetryAfter(("retry-after-ms", "x"), ("Retry-After", "4")));

    [Fact]
    public void Backoff_FollowsTheJsSchedule()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), RetryTiming.Backoff(0, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5), 0.25, 0));
        Assert.Equal(TimeSpan.FromMilliseconds(750), RetryTiming.Backoff(1, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5), 0.25, 1));
        Assert.Equal(TimeSpan.FromSeconds(5), RetryTiming.Backoff(9, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5), 0.25, 0));
    }
}
