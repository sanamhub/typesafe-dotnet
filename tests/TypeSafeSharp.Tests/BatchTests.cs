using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;

namespace TypeSafeSharp.Tests;

public sealed class BatchTests : IDisposable
{
    private static readonly int[] s_releaseOrder = [2, 0, 1];
    private static readonly string[] s_letters = ["a", "b", "c"];

    private readonly GatedHandler _handler = new();
    private readonly HttpClient _http;

    public BatchTests() => _http = new HttpClient(_handler, disposeHandler: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    private TypeSafeClient Client() => new(_http, ownsHttpClient: false, new TypeSafeClientOptions
    {
        ApiKey = "ts_test_0000000000000000",
        BaseUrl = new Uri("https://api.typesafe.ai"),
        DefaultModel = "jev-latest",
        Retry = RetryPolicy.None,
    }, new FakeTimeProvider(), () => 0);

    // The item's number travels as the state, so the handler can tell items apart.
    private static SystemOneRequest RequestFor(int item)
        => new(item, new Dictionary<string, Question> { ["is_urgent"] = Question.Noul("Is it urgent?") });

    [Fact]
    public async Task NeverMoreThanMaxConcurrencyInFlight()
    {
        _handler.AutoRelease = true;
        using var client = Client();

        var results = new List<BatchItem<int>>();
        await foreach (var result in client.EvaluateManyAsync(Enumerable.Range(0, 12), RequestFor, new BatchOptions { MaxConcurrency = 3 }, Ct))
        {
            results.Add(result);
        }

        Assert.Equal(12, results.Count);
        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(3, _handler.MaxInFlight);
    }

    [Fact]
    public async Task YieldsInCompletionOrderWithInputIndex()
    {
        using var client = Client();
        var enumerator = client.EvaluateManyAsync(s_letters, s => RequestFor(s[0] - 'a'), Ct).GetAsyncEnumerator(Ct);
        try
        {
            // An async iterator starts on the first MoveNextAsync, so ask before waiting for requests.
            var next = enumerator.MoveNextAsync();
            await _handler.WaitForInFlightAsync(3);
            foreach (var expected in s_releaseOrder)
            {
                if (expected != s_releaseOrder[0])
                {
                    next = enumerator.MoveNextAsync();
                }

                _handler.Release(expected);
                Assert.True(await next);
                Assert.Equal(expected, enumerator.Current.Index);
                Assert.Equal(((char)('a' + expected)).ToString(), enumerator.Current.Item);
                Assert.Equal(0.95, enumerator.Current.Response!.GetNoul("is_urgent").Noul);
            }

            Assert.False(await enumerator.MoveNextAsync());
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task ItemFailures_AreYielded()
    {
        _handler.AutoRelease = true;
        _handler.StatusFor[1] = (HttpStatusCode)422;
        using var client = Client();

        var results = new List<BatchItem<int>>();
        await foreach (var result in client.EvaluateManyAsync(Enumerable.Range(0, 3), RequestFor, Ct))
        {
            results.Add(result);
        }

        var failed = Assert.Single(results, r => !r.Succeeded);
        Assert.Equal(1, failed.Index);
        Assert.IsType<TypeSafeUnprocessableEntityException>(failed.Exception);
        Assert.Null(failed.Response);
        Assert.Equal(2, results.Count(r => r.Succeeded));
    }

    [Fact]
    public async Task CreateRequestArgumentError_FailsThatItemOnly()
    {
        _handler.AutoRelease = true;
        using var client = Client();

        var results = new List<BatchItem<int>>();
        await foreach (var result in client.EvaluateManyAsync(
            Enumerable.Range(0, 3),
            i => i == 0 ? new SystemOneRequest("x", new Dictionary<string, Question>()) : RequestFor(i),
            Ct))
        {
            results.Add(result);
        }

        Assert.IsType<ArgumentException>(Assert.Single(results, r => !r.Succeeded).Exception);
        Assert.Equal(2, _handler.Requests);
    }

    [Fact]
    public async Task Unauthorized_EndsTheEnumeration()
    {
        _handler.AutoRelease = true;
        _handler.StatusFor[0] = HttpStatusCode.Unauthorized;
        _handler.StatusFor[1] = HttpStatusCode.Unauthorized;
        using var client = Client();

        await Assert.ThrowsAsync<TypeSafeAuthenticationException>(async () =>
        {
            await foreach (var _ in client.EvaluateManyAsync(Enumerable.Range(0, 100), RequestFor, new BatchOptions { MaxConcurrency = 2 }, Ct))
            {
            }
        });

        Assert.True(_handler.Requests < 100, "the batch should stop, not send every item");
    }

    [Fact]
    public async Task Cancellation_StopsWithin100Ms()
    {
        using var client = Client();
        using var cts = new CancellationTokenSource();
        var enumerator = client.EvaluateManyAsync(Enumerable.Range(0, 10), RequestFor, cts.Token).GetAsyncEnumerator(Ct);
        try
        {
            var next = enumerator.MoveNextAsync();
            await _handler.WaitForInFlightAsync(4);
            var clock = Stopwatch.StartNew();

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await next);
            Assert.True(clock.ElapsedMilliseconds < 100, $"took {clock.ElapsedMilliseconds} ms");
            Assert.Equal(4, _handler.CancelledTokens);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task BreakingOut_CancelsInFlightCalls()
    {
        using var client = Client();
        var enumerator = client.EvaluateManyAsync(Enumerable.Range(0, 10), RequestFor, Ct).GetAsyncEnumerator(Ct);
        var next = enumerator.MoveNextAsync();
        await _handler.WaitForInFlightAsync(4);
        _handler.Release(0);
        Assert.True(await next);

        await enumerator.DisposeAsync();

        // The slot item 0 freed is refilled only when the caller asks for the next item, so 3 remain.
        Assert.Equal(3, _handler.CancelledTokens);
        Assert.Equal(4, _handler.Requests);
    }

    [Fact]
    public void ArgumentErrors_ThrowAtTheCall()
    {
        using var client = Client();

        Assert.Throws<ArgumentNullException>(() => client.EvaluateManyAsync<int>(null!, RequestFor, Ct));
        Assert.Throws<ArgumentNullException>(() => client.EvaluateManyAsync(Enumerable.Range(0, 1), null!, Ct));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.EvaluateManyAsync(Enumerable.Range(0, 1), RequestFor, new BatchOptions { MaxConcurrency = 0 }, Ct));
    }

    [Fact]
    public async Task MockOverridingSystemOneAsync_DrivesTheBatch()
    {
        using var mock = new AnsweringClient();

        var results = new List<BatchItem<int>>();
        await foreach (var result in mock.EvaluateManyAsync(Enumerable.Range(0, 5), RequestFor, Ct))
        {
            results.Add(result);
        }

        Assert.Equal([0, 1, 2, 3, 4], results.Select(r => r.Index).OrderBy(i => i));
        Assert.All(results, r => Assert.Equal(0.5, r.Response!.GetNoul("q").Noul));
    }

    private sealed class AnsweringClient : TypeSafeClient
    {
        public override Task<SystemOneResponse> SystemOneAsync(SystemOneRequest request, TypeSafeRequestOptions? options, CancellationToken cancellationToken = default)
            => Task.FromResult(TypeSafeModelFactory.SystemOneResponse(
                "jev-1.13.0",
                new Dictionary<string, Answer> { ["q"] = TypeSafeModelFactory.NoulAnswer(0.5) },
                TypeSafeModelFactory.Usage(1, 1)));
    }

    // Holds each request until the test releases its item (or AutoRelease is on), counting how
    // many are in flight at once and how many saw their token cancelled.
    private sealed class GatedHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _gates = new();
        private readonly object _gate = new();
        private int _inFlight;

        public bool AutoRelease { get; set; }

        public ConcurrentDictionary<int, HttpStatusCode> StatusFor { get; } = new();

        public int MaxInFlight { get; private set; }

        public int Requests { get; private set; }

        public int CancelledTokens { get; private set; }

        public void Release(int item) => Gate(item).TrySetResult(true);

        public async Task WaitForInFlightAsync(int count)
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                lock (_gate)
                {
                    if (_inFlight >= count)
                    {
                        return;
                    }
                }

                if (clock.Elapsed > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException($"only {_inFlight} of {count} requests arrived");
                }

                await Task.Delay(5, Ct);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsByteArrayAsync())!;
            var item = body["state"]!.GetValue<int>();
            lock (_gate)
            {
                Requests++;
                _inFlight++;
                MaxInFlight = Math.Max(MaxInFlight, _inFlight);
            }

            try
            {
                if (AutoRelease)
                {
                    // Long enough for the batch to fill every slot before the first finishes.
                    await Task.Delay(20, cancellationToken);
                }
                else
                {
                    using (cancellationToken.Register(() => Gate(item).TrySetCanceled(cancellationToken)))
                    {
                        await Gate(item).Task;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                lock (_gate)
                {
                    CancelledTokens++;
                }

                throw;
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight--;
                }
            }

            var status = StatusFor.TryGetValue(item, out var s) ? s : HttpStatusCode.OK;
            var json = status == HttpStatusCode.OK ? Encoding.UTF8.GetString(Fixture.Bytes("responses/noul.json")) : "{}";
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private TaskCompletionSource<bool> Gate(int item)
            => _gates.GetOrAdd(item, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
