using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace TypeSafeSharp.Tests;

public sealed class LoggingTests : IDisposable
{
    private const string Key = "ts_test_ZZZZ_marker_key";
    private const string Marker = "MARKER_STATE_TEXT";

    private readonly StubHandler _stub = new();
    private readonly HttpClient _http;
    private readonly CapturingLoggerFactory _logs = new();

    public LoggingTests() => _http = new HttpClient(_stub, disposeHandler: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _http.Dispose();
        _stub.Dispose();
        _logs.Dispose();
    }

    private TypeSafeClient Client(RetryPolicy? retry = null)
        => new(_http, ownsHttpClient: false, new TypeSafeClientOptions
        {
            ApiKey = Key,
            BaseUrl = new Uri("https://api.typesafe.ai"),
            DefaultModel = "jev-latest",
            Retry = retry ?? RetryPolicy.Default,
            LoggerFactory = _logs,
        }, new FakeTimeProvider(), () => 0);

    private static SystemOneRequest Request()
        => new(Marker + " in the state", new Dictionary<string, Question> { ["is_urgent"] = Question.Noul(Marker + " in the instructions") });

    [Fact]
    public async Task Success_LogsStartedAndCompleted()
    {
        _stub.RespondFixture("noul.json", "req_1");
        using var client = Client();

        await client.SystemOneAsync(Request(), Ct);

        Assert.Equal([1, 2], _logs.Entries.Select(e => e.EventId));
        Assert.Equal(LogLevel.Debug, _logs.Entries[0].Level);
        Assert.Equal("POST /v1/systemone attempt 1 started", _logs.Entries[0].Message);
        Assert.Equal(LogLevel.Information, _logs.Entries[1].Level);
        Assert.StartsWith("POST /v1/systemone 200 in ", _logs.Entries[1].Message, StringComparison.Ordinal);
        Assert.EndsWith(" ms, request req_1", _logs.Entries[1].Message, StringComparison.Ordinal);
        Assert.All(_logs.Entries, e => Assert.Equal("TypeSafeSharp", e.Category));
    }

    [Fact]
    public async Task Retry_LogsWarning3()
    {
        _stub.Respond((HttpStatusCode)429, "{}", r => r.Headers.TryAddWithoutValidation("retry-after-ms", "0"));
        _stub.RespondFixture("noul.json");
        using var client = Client();

        await client.SystemOneAsync(Request(), Ct);

        var retry = Assert.Single(_logs.Entries, e => e.EventId == 3);
        Assert.Equal(LogLevel.Warning, retry.Level);
        Assert.Equal("Retrying POST /v1/systemone after 429, retry 1 of 2, waiting 0 ms", retry.Message);
    }

    [Fact]
    public async Task Failure_LogsWarning5()
    {
        _stub.Respond(HttpStatusCode.Unauthorized, """{"error":"invalid api key"}""", r => r.Headers.Add("x-typesafe-request-id", "req_401"));
        using var client = Client();

        await Assert.ThrowsAsync<TypeSafeAuthenticationException>(() => client.SystemOneAsync(Request(), Ct));

        var failed = Assert.Single(_logs.Entries, e => e.EventId == 5);
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Equal("POST /v1/systemone failed after 1 attempts: 401, request req_401", failed.Message);
    }

    [Fact]
    public async Task Timeout_LogsReasonAsTimeout()
    {
        _stub.Throw(new TaskCanceledException("HttpClient.Timeout"));
        using var client = Client(RetryPolicy.None);

        await Assert.ThrowsAsync<TypeSafeTimeoutException>(() => client.SystemOneAsync(Request(), Ct));

        Assert.Contains(": timeout, request ", Assert.Single(_logs.Entries, e => e.EventId == 5).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownKind_LogsOncePerClient()
    {
        _stub.RespondFixture("unknown-kind.json");
        _stub.RespondFixture("unknown-kind.json");
        using var client = Client();

        await client.SystemOneAsync(Request(), Ct);
        await client.SystemOneAsync(Request(), Ct);

        var unknown = Assert.Single(_logs.Entries, e => e.EventId == 4);
        Assert.Equal(LogLevel.Warning, unknown.Level);
        Assert.Equal("Unknown answer kind ranking for question rank; returned as UnknownAnswer", unknown.Message);
    }

    [Fact]
    public async Task Models_AreLoggedWithTheirEndpoint()
    {
        _stub.RespondFixture("models.json");
        using var client = Client();

        await client.Models.ListAsync(Ct);

        Assert.Equal("GET /v1/models attempt 1 started", _logs.Entries[0].Message);
    }

    [Fact]
    public async Task NothingSensitive_EvenAtTrace()
    {
        // A 422 that echoes the state, a retry and a success: every path that logs.
        _stub.Respond((HttpStatusCode)503, "{}", r => r.Headers.TryAddWithoutValidation("retry-after-ms", "0"));
        _stub.Respond((HttpStatusCode)422, $$"""{"detail":[{"loc":["body","state"],"msg":"bad","type":"t","input":"{{Marker}}"}]}""");
        _stub.RespondFixture("unknown-kind.json");
        using var client = Client();

        await Assert.ThrowsAsync<TypeSafeUnprocessableEntityException>(() => client.SystemOneAsync(Request(), Ct));
        await client.SystemOneAsync(Request(), Ct);

        Assert.NotEmpty(_logs.Entries);
        foreach (var entry in _logs.Entries)
        {
            foreach (var text in entry.AllText())
            {
                Assert.DoesNotContain(Marker, text, StringComparison.Ordinal);
                Assert.DoesNotContain("ZZZZ", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Bearer", text, StringComparison.Ordinal);
            }
        }
    }

    internal sealed class Entry(string category, LogLevel level, int eventId, string message, IReadOnlyList<KeyValuePair<string, object?>> state)
    {
        public string Category { get; } = category;

        public LogLevel Level { get; } = level;

        public int EventId { get; } = eventId;

        public string Message { get; } = message;

        public IReadOnlyList<KeyValuePair<string, object?>> State { get; } = state;

        public IEnumerable<string> AllText()
            => new[] { Message }.Concat(State.Select(p => p.Key + "=" + p.Value));
    }

    // Records everything at every level; tests read the entries afterwards.
    internal sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentQueue<Entry> _entries = new();

        public List<Entry> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<Entry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var pairs = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
                entries.Enqueue(new Entry(category, logLevel, eventId.Id, formatter(state, exception), pairs));
            }
        }
    }
}
