using System.Net;

namespace TypeSafeSharp.Tests;

public sealed class ExceptionTests
{
    public static TheoryData<int, Type> StatusTypes => new()
    {
        { 400, typeof(TypeSafeBadRequestException) },
        { 401, typeof(TypeSafeAuthenticationException) },
        { 403, typeof(TypeSafePermissionDeniedException) },
        { 404, typeof(TypeSafeNotFoundException) },
        { 422, typeof(TypeSafeUnprocessableEntityException) },
        { 429, typeof(TypeSafeRateLimitException) },
        { 529, typeof(TypeSafeOverloadedException) },
        { 500, typeof(TypeSafeInternalServerException) },
        { 503, typeof(TypeSafeInternalServerException) },
        { 599, typeof(TypeSafeInternalServerException) },
        { 302, typeof(TypeSafeApiException) },
        { 409, typeof(TypeSafeApiException) },
        { 600, typeof(TypeSafeApiException) },
    };

    [Theory]
    [MemberData(nameof(StatusTypes))]
    public void ApiException_ReturnsTypeForStatus(int status, Type expected)
    {
        var ex = TypeSafeModelFactory.ApiException(status);

        Assert.Equal(expected, ex.GetType());
        Assert.Equal((HttpStatusCode)status, ex.StatusCode);
        Assert.Equal("POST /v1/systemone", ex.Endpoint);
        Assert.Empty(ex.Headers);
        Assert.Null(ex.Body);
    }

    [Fact]
    public void ApiException_Overloaded_IsAnInternalServerError()
        => Assert.IsAssignableFrom<TypeSafeInternalServerException>(TypeSafeModelFactory.ApiException(529));

    [Fact]
    public void ApiException_RoundTripsRetryAfterAndRequestId()
    {
        var ex = Assert.IsType<TypeSafeRateLimitException>(
            TypeSafeModelFactory.ApiException(429, "slow down", "req_123", TimeSpan.FromSeconds(3)));

        Assert.Equal(TimeSpan.FromSeconds(3), ex.RetryAfter);
        Assert.Equal("req_123", ex.RequestId);
        Assert.Equal("slow down", ex.Message);
    }

    [Fact]
    public void ApiException_WithoutMessage_SaysThereWasNone()
        => Assert.Equal("503 status code (no message in body)", TypeSafeModelFactory.ApiException(503).Message);

    [Fact]
    public void UnprocessableEntity_FromPublicConstructor_HasNoValidationErrors()
        => Assert.Empty(new TypeSafeUnprocessableEntityException().ValidationErrors);

    [Fact]
    public void ApiException_FromPublicConstructor_HasEmptyHeaders()
        => Assert.Empty(new TypeSafeApiException("x").Headers);

    [Fact]
    public void ResponseValidation_MessageNamesPathAndProblem()
    {
        var ex = new TypeSafeResponseValidationException("$.usage.input_tokens", "expected an integer");

        Assert.Equal("$.usage.input_tokens", ex.JsonPath);
        Assert.Equal("The response did not match the API contract at $.usage.input_tokens: expected an integer.", ex.Message);
    }

    [Fact]
    public void Timeout_IsAConnectionException()
    {
        var ex = new TypeSafeTimeoutException("timed out", TimeSpan.FromSeconds(30), innerException: null);

        Assert.IsAssignableFrom<TypeSafeConnectionException>(ex);
        Assert.Equal(TimeSpan.FromSeconds(30), ex.Timeout);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void NoExceptionType_IsSerializable()
    {
        var types = typeof(TypeSafeException).Assembly.GetTypes()
            .Where(t => typeof(Exception).IsAssignableFrom(t));

        Assert.All(types, t => Assert.False(t.IsDefined(typeof(SerializableAttribute), inherit: false), t.Name));
    }

    [Fact]
    public void NotNull_Null_ThrowsWithArgumentExpression()
    {
        string? value = null;

        var ex = Assert.Throws<ArgumentNullException>(() => Guard.NotNull(value));

        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public void NotNull_Value_ReturnsIt()
        => Assert.Equal("x", Guard.NotNull("x"));

    public static TheoryData<TimeSpan> BadDurations => new()
    {
        TimeSpan.Zero,
        Timeout.InfiniteTimeSpan,
        TimeSpan.FromMilliseconds(-5),
        TimeSpan.MaxValue,
        TimeSpan.FromMilliseconds(int.MaxValue + 1.0),
    };

    [Theory]
    [MemberData(nameof(BadDurations))]
    public void Positive_Rejects(TimeSpan value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Guard.Positive(value));

        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public void Positive_AcceptsLargestTimerValue()
        => Assert.Equal(TimeSpan.FromMilliseconds(int.MaxValue), Guard.Positive(TimeSpan.FromMilliseconds(int.MaxValue)));
}
