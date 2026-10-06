using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Time.Testing;

namespace TypeSafeSharp.Tests;

// The listener is process-wide, so each test asks for a model name of its own and reads only
// activities carrying it.
public sealed class TelemetryTests : IDisposable
{
    private const string Marker = "MARKER_STATE_TEXT";

    private readonly StubHandler _stub = new();
    private readonly HttpClient _http;

    public TelemetryTests() => _http = new HttpClient(_stub, disposeHandler: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _http.Dispose();
        _stub.Dispose();
    }

    private TypeSafeClient Client() => new(_http, ownsHttpClient: false, new TypeSafeClientOptions
    {
        ApiKey = "ts_test_0000000000000000",
        BaseUrl = new Uri("https://api.typesafe.ai"),
        Retry = RetryPolicy.None,
    }, new FakeTimeProvider(), () => 0);

    private static SystemOneRequest Request(string model)
        => new(Marker + " state", new Dictionary<string, Question> { ["is_urgent"] = Question.Noul(Marker + " instructions") }) { Model = model };

    [Fact]
    public async Task Success_SetsTheGenAiTags()
    {
        using var listener = new Listener();
        _stub.RespondFixture("noul.json", "req_ok");
        using var client = Client();

        await client.SystemOneAsync(Request("telemetry-success"), Ct);

        var activity = Assert.Single(listener.For("telemetry-success"));
        Assert.Equal("systemone telemetry-success", activity.DisplayName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("typesafe", activity.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("systemone", activity.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("jev-1.13.0", activity.GetTagItem("gen_ai.response.model"));
        Assert.Equal(296L, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(20L, activity.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Equal("api.typesafe.ai", activity.GetTagItem("server.address"));
        Assert.Equal(443, activity.GetTagItem("server.port"));
        Assert.Equal(200, activity.GetTagItem("http.response.status_code"));
        Assert.Equal("req_ok", activity.GetTagItem("typesafe.request_id"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task RateLimit_MarksTheActivityFailed()
    {
        using var listener = new Listener();
        _stub.Respond((HttpStatusCode)429, "{}", r => r.Headers.Add("x-typesafe-request-id", "req_429"));
        using var client = Client();

        await Assert.ThrowsAsync<TypeSafeRateLimitException>(() => client.SystemOneAsync(Request("telemetry-429"), Ct));

        var activity = Assert.Single(listener.For("telemetry-429"));
        Assert.Equal("429", activity.GetTagItem("error.type"));
        Assert.Equal(429, activity.GetTagItem("http.response.status_code"));
        Assert.Equal("req_429", activity.GetTagItem("typesafe.request_id"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task ConnectionFailure_UsesTheExceptionType()
    {
        using var listener = new Listener();
        _stub.Throw(new HttpRequestException("refused"));
        using var client = Client();

        await Assert.ThrowsAsync<TypeSafeConnectionException>(() => client.SystemOneAsync(Request("telemetry-connection"), Ct));

        Assert.Equal("TypeSafeSharp.TypeSafeConnectionException", Assert.Single(listener.For("telemetry-connection")).GetTagItem("error.type"));
    }

    [Fact]
    public async Task NoTagCarriesContent()
    {
        // AC-3.9.
        using var listener = new Listener();
        _stub.RespondFixture("noul.json");
        using var client = Client();

        await client.SystemOneAsync(Request("telemetry-content"), Ct);

        var activity = Assert.Single(listener.For("telemetry-content"));
        Assert.All(activity.TagObjects, tag => Assert.DoesNotContain(Marker, tag.Value?.ToString() ?? string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain(Marker, activity.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAListener_NoActivityIsCreated()
        => Assert.Null(Telemetry.StartSystemOne("jev-latest", new Uri("https://api.typesafe.ai")));

    private sealed class Listener : IDisposable
    {
        private readonly ConcurrentQueue<Activity> _stopped = new();
        private readonly ActivityListener _listener;

        public Listener()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "TypeSafeSharp",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> For(string model)
            => [.. _stopped.Where(a => (a.GetTagItem("gen_ai.request.model") as string) == model)];

        public void Dispose() => _listener.Dispose();
    }
}
