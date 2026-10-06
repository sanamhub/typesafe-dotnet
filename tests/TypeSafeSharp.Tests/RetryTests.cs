using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Time.Testing;

namespace TypeSafeSharp.Tests;

// PLAN.md section 5.2, one theory row per scenario (AC-5.2). The fake clock records every timer
// the client creates, so each delay is checked exactly, then advanced one tick short to prove the
// next attempt does not start early.
public sealed class RetryTests : IDisposable
{
    private const string TestKey = "ts_test_0000000000000000";
    private static readonly TimeSpan s_tick = TimeSpan.FromTicks(1);

    private readonly StubHandler _stub = new();
    private readonly RecordingTimeProvider _time = new();
    private readonly HttpClient _http;

    public RetryTests() => _http = new HttpClient(_stub, disposeHandler: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _http.Dispose();
        _stub.Dispose();
        _time.Dispose();
    }

    private static readonly Dictionary<string, Scenario> s_scenarios = new()
    {
        ["429 then 200"] = new([Status(429), Ok()], [Ms(500)]),
        ["429 with retry-after-ms 250"] = new([Status(429, ("retry-after-ms", "250")), Ok()], [Ms(250)]),
        ["429 with Retry-After 2"] = new([Status(429, ("Retry-After", "2")), Ok()], [Ms(2000)]),
        ["429 with Retry-After date 3 s ahead"] = new([Status(429, ("Retry-After", RetryTests.HttpDate(3))), Ok()], [Ms(3000)]),
        ["429 with Retry-After 120 uses backoff"] = new([Status(429, ("Retry-After", "120")), Ok()], [Ms(500)]),
        ["529 three times"] = new([Status(529), Status(529), Status(529)], [Ms(500), Ms(1000)], typeof(TypeSafeOverloadedException)),
        ["500 retried"] = new([Status(500), Ok()], [Ms(500)]),
        ["502 retried"] = new([Status(502), Ok()], [Ms(500)]),
        ["503 retried"] = new([Status(503), Ok()], [Ms(500)]),
        ["504 retried"] = new([Status(504), Ok()], [Ms(500)]),
        ["408 retried"] = new([Status(408), Ok()], [Ms(500)]),
        ["400 not retried"] = new([Status(400)], [], typeof(TypeSafeBadRequestException)),
        ["401 not retried"] = new([Status(401)], [], typeof(TypeSafeAuthenticationException)),
        ["403 not retried"] = new([Status(403)], [], typeof(TypeSafePermissionDeniedException)),
        ["404 not retried"] = new([Status(404)], [], typeof(TypeSafeNotFoundException)),
        ["422 not retried"] = new([Status(422)], [], typeof(TypeSafeUnprocessableEntityException)),
        ["connection reset then 200"] = new([Throws(() => new HttpRequestException("reset", new IOException("connection reset"))), Ok()], [Ms(500)]),
        ["IOException then 200"] = new([Throws(() => new IOException("broken pipe")), Ok()], [Ms(500)]),
        ["attempt timeout then 200"] = new([TimesOut(), Ok()], [Ms(500)]),
        ["attempt timeout exhausted"] = new([TimesOut(), TimesOut(), TimesOut()], [Ms(500), Ms(1000)], typeof(TypeSafeTimeoutException)),
        ["attempt timeout, RetryOnTimeout false"] = new([TimesOut()], [], typeof(TypeSafeTimeoutException), o => o.Retry = new RetryPolicy { RetryOnTimeout = false }),
        ["backoff 500 then 1000"] = new([Status(500), Status(500), Ok()], [Ms(500), Ms(1000)]),
        ["backoff minus full jitter"] = new([Status(500), Status(500), Ok()], [Ms(375), Ms(750)], jitter: 1),
        ["backoff capped at 5 s"] = new(
            [Status(500), Status(500), Status(500), Status(500), Status(500), Ok()],
            [Ms(500), Ms(1000), Ms(2000), Ms(4000), Ms(5000)],
            configure: o => { o.Retry = new RetryPolicy { MaxRetries = 5 }; o.TotalTimeout = TimeSpan.FromSeconds(60); }),
        ["budget 1 s, wait 2 s: no retry"] = new([Status(429, ("Retry-After", "2"))], [], typeof(TypeSafeRateLimitException), o => o.TotalTimeout = TimeSpan.FromSeconds(1), attemptLimit: TimeSpan.FromSeconds(1)),
        ["budget shorter than attempt timeout"] = new([TimesOut()], [], typeof(TypeSafeTimeoutException), o => o.TotalTimeout = TimeSpan.FromSeconds(3), attemptLimit: TimeSpan.FromSeconds(3)),
        ["RetryPolicy.None"] = new([Status(429)], [], typeof(TypeSafeRateLimitException), o => o.Retry = RetryPolicy.None),
        ["handler chain exception not retried"] = new([Throws(() => new InvalidOperationException("circuit open"))], [], typeof(TypeSafeConnectionException)),
        ["server hint not reused after a timeout"] = new([Status(429, ("Retry-After", "2")), TimesOut(), Ok()], [Ms(2000), Ms(1000)]),
    };

    public static TheoryData<string> Scenarios => new(s_scenarios.Keys);

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Retry(string name)
    {
        var scenario = s_scenarios[name];
        foreach (var step in scenario.Steps)
        {
            step.Enqueue(_stub);
        }

        using var client = Client(scenario.Configure, scenario.Jitter);
        var call = client.SystemOneAsync(Request(), Ct);
        for (var i = 0; i < scenario.Steps.Length; i++)
        {
            var attemptTimer = await _time.NextTimerAsync();
            await _stub.WaitForRequestAsync();
            if (i == 0)
            {
                Assert.Equal(scenario.AttemptLimit ?? TimeSpan.FromSeconds(10), attemptTimer);
            }

            if (_stub.Requests.Count != i + 1)
            {
                Assert.Fail($"expected attempt {i + 1}, saw {_stub.Requests.Count}");
            }

            if (scenario.Steps[i].TimesOut)
            {
                _time.Advance(attemptTimer);
            }

            if (i < scenario.Delays.Length)
            {
                var delay = await _time.NextTimerAsync();
                Assert.Equal(scenario.Delays[i], delay);
                _time.Advance(delay - s_tick);
                await Task.Delay(15, Ct);
                Assert.Equal(i + 1, _stub.Requests.Count);
                _time.Advance(s_tick);
            }
        }

        if (scenario.Error is null)
        {
            Assert.Equal(0.95, (await call).GetNoul("is_urgent").Noul);
        }
        else
        {
            Assert.IsType(scenario.Error, await Assert.ThrowsAnyAsync<TypeSafeException>(() => call));
        }

        Assert.Equal(scenario.Steps.Length, _stub.Requests.Count);
        for (var i = 0; i < _stub.Requests.Count; i++)
        {
            var headers = _stub.Requests[i].Headers;
            if (i == 0)
            {
                Assert.False(headers.ContainsKey("X-TypeSafe-Retry-Count"));
            }
            else
            {
                Assert.Equal(i.ToString(CultureInfo.InvariantCulture), headers["X-TypeSafe-Retry-Count"]);
            }
        }
    }

    [Fact]
    public async Task CallerCancelsDuringDelay_NoFurtherAttempt()
    {
        Status(429).Enqueue(_stub);
        Ok().Enqueue(_stub);
        using var cts = new CancellationTokenSource();
        using var client = Client();
        var call = client.SystemOneAsync(Request(), cts.Token);
        await _time.NextTimerAsync();
        await _stub.WaitForRequestAsync();
        await _time.NextTimerAsync();

        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(cts.Token, ex.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.Single(_stub.Requests);
    }

    [Fact]
    public async Task DisposeDuringDelay_ThrowsObjectDisposed()
    {
        Status(503).Enqueue(_stub);
        Ok().Enqueue(_stub);
        var client = Client(ownsHttpClient: true);
        var call = client.SystemOneAsync(Request(), Ct);
        await _time.NextTimerAsync();
        await _stub.WaitForRequestAsync();
        var delay = await _time.NextTimerAsync();

        client.Dispose();
        _time.Advance(delay);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => call);
        Assert.Single(_stub.Requests);
    }

    [Fact]
    public async Task PerCallRetry_ReplacesTheClientPolicyWhole()
    {
        Status(500).Enqueue(_stub);
        using var client = Client(o => o.Retry = new RetryPolicy { MaxRetries = 5 });

        await Assert.ThrowsAsync<TypeSafeInternalServerException>(
            () => client.SystemOneAsync(Request(), new TypeSafeRequestOptions { Retry = RetryPolicy.None }, Ct));
        Assert.Single(_stub.Requests);
    }

    private TypeSafeClient Client(Action<TypeSafeClientOptions>? configure = null, double jitter = 0, bool ownsHttpClient = false)
    {
        var options = new TypeSafeClientOptions { ApiKey = TestKey, BaseUrl = new Uri("https://api.typesafe.ai"), DefaultModel = "jev-latest" };
        configure?.Invoke(options);
        return new TypeSafeClient(_http, ownsHttpClient, options, _time, () => jitter);
    }

    private static SystemOneRequest Request()
        => new("text", new Dictionary<string, Question> { ["is_urgent"] = Question.Noul("Does this convey urgency?") });

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    // FakeTimeProvider starts at a fixed instant, so a date 3 s ahead of it is exactly 3000 ms away.
    private static string HttpDate(int secondsAhead)
    {
        return new FakeTimeProvider().GetUtcNow().AddSeconds(secondsAhead).ToString("R", CultureInfo.InvariantCulture);
    }

    private static Step Ok() => new(stub => stub.RespondFixture("noul.json"));

    private static Step Status(int status, params (string Name, string Value)[] headers)
        => new(stub => stub.Respond((HttpStatusCode)status, "{}", response =>
        {
            foreach (var (headerName, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(headerName, value);
            }
        }));

    private static Step Throws(Func<Exception> exception) => new(stub => stub.Throw(exception()));

    private static Step TimesOut() => new(stub => stub.WaitForCancellation(), timesOut: true);

    // One queued handler behaviour. A step that times out never answers; the test fires the attempt timer.
    private sealed class Step(Action<StubHandler> enqueue, bool timesOut = false)
    {
        public bool TimesOut { get; } = timesOut;

        public void Enqueue(StubHandler stub) => enqueue(stub);
    }

    private sealed class Scenario(
        Step[] steps,
        TimeSpan[] delays,
        Type? error = null,
        Action<TypeSafeClientOptions>? configure = null,
        double jitter = 0,
        TimeSpan? attemptLimit = null)
    {
        public Step[] Steps { get; } = steps;

        public TimeSpan[] Delays { get; } = delays;

        public Type? Error { get; } = error;

        public Action<TypeSafeClientOptions>? Configure { get; } = configure;

        public double Jitter { get; } = jitter;

        public TimeSpan? AttemptLimit { get; } = attemptLimit;
    }
}

// Records the due time of every timer the client creates, in order, and lets a test wait for the next one.
internal sealed class RecordingTimeProvider : FakeTimeProvider, IDisposable
{
    private readonly ConcurrentQueue<TimeSpan> _dueTimes = new();
    private readonly SemaphoreSlim _created = new(0);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _dueTimes.Enqueue(dueTime);
        _created.Release();
        return base.CreateTimer(callback, state, dueTime, period);
    }

    public async Task<TimeSpan> NextTimerAsync()
    {
        await _created.WaitAsync(TestContext.Current.CancellationToken);
        return _dueTimes.TryDequeue(out var due) ? due : throw new InvalidOperationException("timer signalled but not recorded");
    }

    public void Dispose() => _created.Dispose();
}
