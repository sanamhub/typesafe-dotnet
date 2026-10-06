using System.Net;
using System.Net.Http;
using System.Text;

namespace TypeSafeSharp.Tests;

// Answers requests from a queue and records what was sent. Each queued step sees the request and
// its token, so a test can return a response, throw, or wait for cancellation.
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps = new();
    private readonly object _gate = new();
    // Counts requests, so a test that starts waiting after the request arrived still sees it.
    private readonly SemaphoreSlim _requestsSeen = new(0);

    public List<RecordedRequest> Requests { get; } = [];

    public void Respond(HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null)
        => Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return Task.FromResult(response);
        });

    public void RespondFixture(string fixture, string? requestId = null)
        => Respond(HttpStatusCode.OK, Encoding.UTF8.GetString(Fixture.Bytes("responses/" + fixture)), response =>
        {
            if (requestId is not null)
            {
                response.Headers.Add("x-typesafe-request-id", requestId);
            }
        });

    public void Throw(Exception exception) => Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));

    // Blocks until the request's token is cancelled, then throws what HttpClient would, or what the test chooses.
    public void WaitForCancellation(Func<Exception>? thenThrow = null)
        => Enqueue(async (_, token) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (thenThrow is not null)
            {
                throw thenThrow();
            }

            throw new InvalidOperationException("unreachable");
        });

    public void Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step)
    {
        lock (_gate)
        {
            _steps.Enqueue(step);
        }
    }

    // Completes once a request has reached the handler that no earlier wait consumed.
    public Task WaitForRequestAsync() => _requestsSeen.WaitAsync(TestContext.Current.CancellationToken);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step;
        lock (_gate)
        {
            Requests.Add(new RecordedRequest(request, body));
            step = _steps.Count > 0 ? _steps.Dequeue() : throw new InvalidOperationException("StubHandler has no response queued.");
        }

        _requestsSeen.Release();
        return await step(request, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _requestsSeen.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class RecordedRequest
{
    public RecordedRequest(HttpRequestMessage request, byte[]? body)
    {
        Method = request.Method;
        Uri = request.RequestUri!;
        Version = request.Version;

        // VersionPolicy exists on net10.0 only; read by reflection so the tests need no #if.
        VersionPolicy = request.GetType().GetProperty("VersionPolicy")?.GetValue(request)?.ToString();
        ExpectContinue = request.Headers.ExpectContinue;
        Body = body;
        foreach (var header in request.Headers)
        {
            Headers[header.Key] = string.Join(", ", header.Value);
        }

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                Headers[header.Key] = string.Join(", ", header.Value);
            }
        }
    }

    public HttpMethod Method { get; }

    public Uri Uri { get; }

    public Version Version { get; }

    public string? VersionPolicy { get; }

    public bool? ExpectContinue { get; }

    public byte[]? Body { get; }

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
}
