using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;

namespace TypeSafeSharp.Tests;

public sealed class ClientTests : IDisposable
{
    private const string TestKey = "ts_test_0000000000000000";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly StubHandler _stub = new();
    private readonly FakeTimeProvider _time = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        _stub.Dispose();
    }

    // Every option the environment could supply is set, so machine settings cannot leak in.
    // No retries: T09 covers one attempt; the retry loop has its own tests.
    private TypeSafeClient Client(Action<TypeSafeClientOptions>? configure = null, bool ownsHttpClient = true, HttpClient? httpClient = null)
    {
        var options = new TypeSafeClientOptions
        {
            ApiKey = TestKey,
            BaseUrl = new Uri("https://api.typesafe.ai"),
            DefaultModel = "jev-latest",
            Retry = RetryPolicy.None,
        };
        configure?.Invoke(options);
        var client = new TypeSafeClient(httpClient ?? Track(new HttpClient(_stub, disposeHandler: false)), ownsHttpClient, options, _time, () => 0.5);
        return Track(client);
    }

    private T Track<T>(T disposable)
        where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    private static SystemOneRequest ScoreRequest()
        => new(
            "Help! My payouts have been failing for 3 days.",
            new Dictionary<string, Question> { ["frustration"] = Question.Score("How frustrated is the customer?", "Calm", "Frustrated", "Very angry") });

    [Fact]
    public async Task SystemOne_RoundTripsTheDocumentedExample()
    {
        _stub.RespondFixture("score.json", "req_abc");

        var response = await Client().SystemOneAsync(ScoreRequest(), Ct);

        var sent = Assert.Single(_stub.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", sent.Uri.AbsoluteUri);
        Fixture.AssertJsonEqual(Fixture.Json("requests/score.json"), sent.Body!);
        Assert.Equal(1, response.GetScore("frustration").MostLikelyLevel);
        Assert.Equal("jev-1.13.0", response.Model);
        Assert.Equal("req_abc", response.RequestId);
    }

    [Fact]
    public async Task SystemOne_ModelOverride_IsSent()
    {
        _stub.RespondFixture("noul.json");
        var request = ScoreRequest();
        request.Model = "jev-1.13.0";

        await Client().SystemOneAsync(request, Ct);

        Assert.Equal("jev-1.13.0", JsonNode.Parse(_stub.Requests[0].Body!)!["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task SystemOne_StateAndQuestionsOverload_UsesDefaultModel()
    {
        _stub.RespondFixture("noul.json");

        await Client(o => o.DefaultModel = "jev-preview").SystemOneAsync("text", new Dictionary<string, Question> { ["q"] = Question.Noul("x") }, Ct);

        Assert.Equal("jev-preview", JsonNode.Parse(_stub.Requests[0].Body!)!["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task BaseUrlWithPath_KeepsThePath()
    {
        _stub.RespondFixture("noul.json");

        await Client(o => o.BaseUrl = new Uri("https://gateway.example/api")).SystemOneAsync(ScoreRequest(), Ct);

        Assert.Equal("https://gateway.example/api/v1/systemone", _stub.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Models_ListsModels()
    {
        _stub.RespondFixture("models.json");

        var models = await Client().Models.ListAsync(Ct);

        Assert.Equal("jev-1.13.0", Assert.Single(models).Name);
        Assert.Equal(HttpMethod.Get, _stub.Requests[0].Method);
        Assert.Equal("https://api.typesafe.ai/v1/models", _stub.Requests[0].Uri.AbsoluteUri);
        Assert.Null(_stub.Requests[0].Body);
    }

    [Fact]
    public async Task Request_CarriesEverySdkHeader()
    {
        _stub.RespondFixture("noul.json");

        await Client().SystemOneAsync(ScoreRequest(), Ct);

        var sent = _stub.Requests[0];
        Assert.True(sent.Headers["Authorization"] == "Bearer " + TestKey);
        Assert.Equal("application/json", sent.Headers["Accept"]);
        Assert.StartsWith("application/json", sent.Headers["Content-Type"], StringComparison.Ordinal);
        Assert.Matches(@"^TypeSafeSharp/\d+\.\d+\.\d+", sent.Headers["User-Agent"]);
        Assert.Equal(sent.Headers["User-Agent"], sent.Headers["X-TypeSafe-SDK"]);
        Assert.DoesNotContain("typesafe-sdk/", sent.Headers["User-Agent"], StringComparison.Ordinal);
        Assert.Matches(new Regex(@"^.+ \((windows|linux|osx|other); [A-Za-z0-9]+\)$"), sent.Headers["X-TypeSafe-Runtime"]);
        Assert.False(sent.Headers.ContainsKey("X-TypeSafe-Retry-Count"));
        Assert.False(sent.ExpectContinue);
    }

    [Fact]
    public async Task Request_AsksForHttp2OnModernDotNetOnly()
    {
        _stub.RespondFixture("noul.json");

        await Client().SystemOneAsync(ScoreRequest(), Ct);

        var sent = _stub.Requests[0];
        if (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal))
        {
            Assert.Equal(new Version(1, 1), sent.Version);
            Assert.Null(sent.VersionPolicy);
        }
        else
        {
            Assert.Equal(new Version(2, 0), sent.Version);
            Assert.Equal("RequestVersionOrLower", sent.VersionPolicy);
        }
    }

    [Fact]
    public async Task CallerDefaultHeaders_DoNotOverrideSdkHeaders()
    {
        _stub.RespondFixture("noul.json");
        var http = Track(new HttpClient(_stub, disposeHandler: false));
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "theirs/1.0");
        http.DefaultRequestHeaders.Add("X-Caller", "kept");

        await Client(ownsHttpClient: false, httpClient: http).SystemOneAsync(ScoreRequest(), Ct);

        Assert.StartsWith("TypeSafeSharp/", _stub.Requests[0].Headers["User-Agent"], StringComparison.Ordinal);
        Assert.Equal("kept", _stub.Requests[0].Headers["X-Caller"]);
    }

    [Fact]
    public async Task Status401_GivesAuthenticationErrorWithRequestId()
    {
        _stub.Respond(HttpStatusCode.Unauthorized, """{"error":"invalid api key"}""", r => r.Headers.Add("x-typesafe-request-id", "req_401"));

        var ex = await Assert.ThrowsAsync<TypeSafeAuthenticationException>(() => Client().SystemOneAsync(ScoreRequest(), Ct));

        Assert.Equal("req_401", ex.RequestId);
        Assert.Equal("POST /v1/systemone", ex.Endpoint);
        Assert.Equal("401 invalid api key", ex.Message);
        Assert.Equal("req_401", ex.Headers["X-TypeSafe-Request-Id"][0]);
        Assert.False(ex.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Redirect_IsNotFollowed()
    {
        _stub.Respond(HttpStatusCode.Found, "", r => r.Headers.Location = new Uri("https://elsewhere.example/"));

        var ex = await Assert.ThrowsAsync<TypeSafeApiException>(() => Client().SystemOneAsync(ScoreRequest(), Ct));

        Assert.Equal(HttpStatusCode.Found, ex.StatusCode);
        Assert.Single(_stub.Requests);
    }

    [Fact]
    public void DefaultHandler_DoesNotFollowRedirects()
    {
        using var handler = HttpDefaults.CreateHandler();

        Assert.Equal(false, handler.GetType().GetProperty("AllowAutoRedirect")!.GetValue(handler));
    }

    [Fact]
    public async Task RateLimit_CarriesRetryAfterWithoutRetries()
    {
        _stub.Respond((HttpStatusCode)429, "{}", r => r.Headers.TryAddWithoutValidation("Retry-After", "3"));

        var ex = await Assert.ThrowsAsync<TypeSafeRateLimitException>(() => Client().SystemOneAsync(ScoreRequest(), Ct));

        Assert.Equal(TimeSpan.FromSeconds(3), ex.RetryAfter);
    }

    [Fact]
    public async Task MalformedSuccessBody_GivesResponseValidationError()
    {
        _stub.Respond(HttpStatusCode.OK, "<html><body>502 Bad Gateway</body></html>");

        var ex = await Assert.ThrowsAsync<TypeSafeResponseValidationException>(() => Client().SystemOneAsync(ScoreRequest(), Ct));

        Assert.Equal("$", ex.JsonPath);
    }

    [Fact]
    public async Task HttpRequestException_GivesConnectionError()
    {
        var cause = new HttpRequestException("No such host is known.");
        _stub.Throw(cause);

        var ex = await Assert.ThrowsAsync<TypeSafeConnectionException>(() => Client().SystemOneAsync(ScoreRequest(), Ct));

        Assert.Same(cause, ex.InnerException);
    }

    [Fact]
    public async Task HandlerChainException_GivesConnectionError()
    {
        // Stands in for Polly's TimeoutRejectedException from a caller's pipeline.
        var cause = new InvalidOperationException("pipeline rejected");
        _stub.Throw(cause);

        var ex = await Assert.ThrowsAsync<TypeSafeConnectionException>(() => Client().SystemOneAsync(ScoreRequest(), Ct));

        Assert.IsNotType<TypeSafeTimeoutException>(ex);
        Assert.Same(cause, ex.InnerException);
    }

    [Fact]
    public async Task AttemptTimer_GivesTimeoutError()
    {
        _stub.WaitForCancellation();
        var call = Client().SystemOneAsync(ScoreRequest(), Ct);
        await _stub.WaitForRequestAsync();

        _time.Advance(TimeSpan.FromSeconds(10));

        var ex = await Assert.ThrowsAsync<TypeSafeTimeoutException>(() => call);
        Assert.Equal(TimeSpan.FromSeconds(10), ex.Timeout);
    }

    [Fact]
    public async Task CallerHttpClientTimeout_GivesTimeoutError()
    {
        _stub.WaitForCancellation();
        var http = Track(new HttpClient(_stub, disposeHandler: false) { Timeout = TimeSpan.FromMilliseconds(50) });

        await Assert.ThrowsAsync<TypeSafeTimeoutException>(() => Client(ownsHttpClient: false, httpClient: http).SystemOneAsync(ScoreRequest(), Ct));
    }

    [Fact]
    public async Task CallerCancellation_ThrowsWithCallerToken()
    {
        _stub.WaitForCancellation();
        using var cts = new CancellationTokenSource();
        var call = Client().SystemOneAsync(ScoreRequest(), cts.Token);
        await _stub.WaitForRequestAsync();

        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task CallerCancellation_WinsOverAHandlerException()
    {
        _stub.WaitForCancellation(() => new InvalidOperationException("pipeline saw the cancellation"));
        using var cts = new CancellationTokenSource();
        var call = Client().SystemOneAsync(ScoreRequest(), cts.Token);
        await _stub.WaitForRequestAsync();

        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task AlreadyCancelledToken_SendsNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _stub.RespondFixture("noul.json");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().SystemOneAsync(ScoreRequest(), cts.Token));
        Assert.Empty(_stub.Requests);
    }

    [Fact]
    public async Task DisposeDuringCall_ThrowsObjectDisposed()
    {
        _stub.WaitForCancellation();
        var http = Track(new HttpClient(_stub, disposeHandler: false));
        var client = Client(ownsHttpClient: true, httpClient: http);
        var call = client.SystemOneAsync(ScoreRequest(), Ct);
        await _stub.WaitForRequestAsync();

        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => call);
        Assert.Single(_stub.Requests);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SystemOneAsync(ScoreRequest(), Ct));
        Assert.Single(_stub.Requests);
        Assert.Throws<ObjectDisposedException>(() => { _ = http.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x.example/"), Ct); });
    }

    [Fact]
    public async Task Dispose_LeavesACallersHttpClientUsable()
    {
        var http = Track(new HttpClient(_stub, disposeHandler: false));
        var client = Client(ownsHttpClient: false, httpClient: http);

        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SystemOneAsync(ScoreRequest(), Ct));
        _stub.Respond(HttpStatusCode.OK, "{}");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://x.example/");
        using var response = await http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PerCallOptions_AreValidatedWhenTheCallStarts()
    {
        var client = Client();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.SystemOneAsync(ScoreRequest(), new TypeSafeRequestOptions { AttemptTimeout = TimeSpan.Zero }, Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.SystemOneAsync(ScoreRequest(), new TypeSafeRequestOptions { Retry = new RetryPolicy { MaxRetries = -1 } }, Ct));
        Assert.Empty(_stub.Requests);
    }

    [Fact]
    public async Task PerCallAttemptTimeout_ReplacesTheClientValue()
    {
        _stub.WaitForCancellation();
        var call = Client().SystemOneAsync(ScoreRequest(), new TypeSafeRequestOptions { AttemptTimeout = TimeSpan.FromSeconds(2) }, Ct);
        await _stub.WaitForRequestAsync();

        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(2), (await Assert.ThrowsAsync<TypeSafeTimeoutException>(() => call)).Timeout);
    }

    [Fact]
    public async Task NullRequest_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => Client().SystemOneAsync(null!, Ct));

    [Fact]
    public void Constructors_ValidateArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new TypeSafeClient((string)null!));
        Assert.Throws<ArgumentNullException>(() => new TypeSafeClient((TypeSafeClientOptions)null!));
        Assert.Throws<ArgumentNullException>(() => new TypeSafeClient(null!, new TypeSafeClientOptions { ApiKey = TestKey }));
        Assert.Throws<TypeSafeConfigurationException>(() => new TypeSafeClient(" "));
    }

    [Fact]
    public void BrowserGuard()
    {
        Assert.Contains("server-side only", Assert.Throws<TypeSafeConfigurationException>(() => TypeSafeClient.ThrowIfBrowser(true)).Message, StringComparison.Ordinal);
        TypeSafeClient.ThrowIfBrowser(false);
    }

    [Fact]
    public async Task MockInstance_ThrowsUntilOverridden()
    {
        var mock = new MockClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => mock.SystemOneAsync(ScoreRequest(), Ct));
        Assert.Throws<InvalidOperationException>(() => mock.Models);
        mock.Dispose();
    }

    private sealed class MockClient : TypeSafeClient
    {
    }
}
