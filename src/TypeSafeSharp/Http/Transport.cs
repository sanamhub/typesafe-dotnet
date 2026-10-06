using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace TypeSafeSharp;

// Sends requests and turns every outcome into a RawResponse or a TypeSafe exception, following
// Appendix C of IMPLEMENTATION.md. Shared by TypeSafeClient and ModelsClient.
internal sealed class Transport
{
    private const string RequestIdHeader = "x-typesafe-request-id";

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _time;
    private readonly Func<double> _jitter;
    private volatile bool _disposed;

    public Transport(HttpClient httpClient, ClientSettings settings, TimeProvider time, Func<double> jitter)
    {
        _httpClient = httpClient;
        Settings = settings;
        _time = time;
        _jitter = jitter;
    }

    public ClientSettings Settings { get; }

    public void MarkDisposed() => _disposed = true;

    public async Task<RawResponse> SendAsync(HttpMethod method, string path, byte[]? body, CallSettings call, CancellationToken cancellationToken)
    {
        // HttpClient does not check an already-cancelled token before handing the request on.
        cancellationToken.ThrowIfCancellationRequested();
        var policy = call.Retry;
        var start = _time.GetTimestamp();
        TypeSafeException? lastError = null;
        for (var retry = 0; ; retry++)
        {
            // The budget covers attempts and waits, so the attempt in flight is cut at it too (ADR-0005).
            var remaining = call.TotalTimeout - _time.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero)
            {
                throw lastError ?? new TypeSafeTimeoutException($"The call used up its total timeout of {Seconds(call.TotalTimeout)} s.", call.TotalTimeout, null);
            }

            var cutByBudget = remaining < call.AttemptTimeout;
            var attemptLimit = cutByBudget ? remaining : call.AttemptTimeout;
            ThrowIfDisposed();

            var attempt = await AttemptAsync(method, path, body, call, retry, attemptLimit, cutByBudget, cancellationToken).ConfigureAwait(false);
            if (attempt.Response is { } response)
            {
                return response;
            }

            var error = attempt.Error!;
            if (!attempt.Retryable || retry >= policy.MaxRetries)
            {
                throw error;
            }

            // A server hint over 60 s falls back to backoff, as in typesafe-sdk-js v0.6.0 src/retry.ts.
            var delay = attempt.ServerDelayMs is { } serverDelay && serverDelay <= RetrySettings.MaxRetryAfter.TotalMilliseconds
                ? TimeSpan.FromMilliseconds(serverDelay)
                : RetryTiming.Backoff(retry, RetrySettings.InitialBackoff, RetrySettings.MaxBackoff, RetrySettings.Jitter, _jitter());
            if (_time.GetElapsedTime(start) + delay >= call.TotalTimeout)
            {
                // The rest of the budget would be spent waiting, so fail now with the real error.
                throw error;
            }

            await Timing.DelayAsync(_time, delay, cancellationToken).ConfigureAwait(false);
            lastError = error;
        }
    }

    private async Task<Attempt> AttemptAsync(
        HttpMethod method,
        string path,
        byte[]? body,
        CallSettings call,
        int retry,
        TimeSpan attemptLimit,
        bool cutByBudget,
        CancellationToken cancellationToken)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        HttpResponseMessage? response = null;
        byte[]? responseBody = null;
        var timer = Timing.CancelAfter(_time, attemptCts, attemptLimit);
        try
        {
            using var request = CreateRequest(method, path, body, retry);
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, attemptCts.Token).ConfigureAwait(false);
#pragma warning disable CA2016 // No token overload on netstandard2.0; ResponseContentRead already buffered the body under the attempt token.
            responseBody = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#pragma warning restore CA2016
        }

        // The order is the rule: caller cancellation wins over everything, then dispose, then timeout.
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (Exception) when (_disposed)
        {
            // HttpClient.Dispose cancels in-flight sends, which would otherwise look like a timeout.
            throw new ObjectDisposedException(nameof(TypeSafeClient));
        }
        catch (OperationCanceledException ex)
        {
            // Our attempt timer, or HttpClient.Timeout on a caller's client.
            var timeout = cutByBudget ? call.TotalTimeout : attemptLimit;
            var message = cutByBudget
                ? $"The call used up its total timeout of {Seconds(timeout)} s."
                : $"The attempt timed out after {Seconds(timeout)} s.";
            return Attempt.Failed(new TypeSafeTimeoutException(message, timeout, ex), call.Retry.RetryOnTimeout && !cutByBudget, "timeout", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return Attempt.Failed(new TypeSafeConnectionException("Could not reach the TypeSafe API: " + ex.Message, ex), true, "connection error", null);
        }
        catch (Exception ex) when (ex is not TypeSafeException)
        {
            // A handler the caller added, such as a Polly timeout or circuit breaker. Their policy, not ours: no retry.
            return Attempt.Failed(new TypeSafeConnectionException("A handler in the HttpClient pipeline failed: " + ex.Message, ex), false, "connection error", null);
        }
        finally
        {
            timer.Dispose();
            if (responseBody is null)
            {
                response?.Dispose();
            }
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var requestId = response.Headers.TryGetValues(RequestIdHeader, out var ids) ? ids.FirstOrDefault() : null;
            var headers = CopyHeaders(response);
            if (status is >= 200 and <= 299)
            {
                return Attempt.Succeeded(new RawResponse(status, responseBody, requestId, headers));
            }

            var serverDelay = RetryTiming.ParseRetryAfterMilliseconds(response.Headers, _time.GetUtcNow());
            return Attempt.Failed(
                ErrorMapper.Create(status, responseBody, call.Endpoint, requestId, headers, serverDelay),
                RetrySettings.IsRetryableStatus(status),
                status.ToString(CultureInfo.InvariantCulture),
                serverDelay);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, byte[]? body, int retry)
    {
        // Appending, not new Uri(base, path), which would drop a gateway's base path.
        var request = new HttpRequestMessage(method, new Uri(Settings.BaseUrl.AbsoluteUri.TrimEnd('/') + path));
        try
        {
            var headers = request.Headers;
            headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey);
            headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            headers.TryAddWithoutValidation("User-Agent", SdkInfo.UserAgent);
            headers.TryAddWithoutValidation("X-TypeSafe-SDK", SdkInfo.UserAgent);
            headers.TryAddWithoutValidation("X-TypeSafe-Runtime", SdkInfo.Runtime);
            if (retry > 0)
            {
                headers.TryAddWithoutValidation("X-TypeSafe-Retry-Count", retry.ToString(CultureInfo.InvariantCulture));
            }

            // Saves a round trip on .NET Framework, which otherwise waits for 100 Continue.
            headers.ExpectContinue = false;
            HttpDefaults.SetVersion(request);
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    // Response headers only, so the Authorization header can never reach an exception.
    private static Dictionary<string, IReadOnlyList<string>> CopyHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            headers[header.Key] = header.Value.ToList().AsReadOnly();
        }

        return headers;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TypeSafeClient));
        }
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed class Attempt
    {
        private Attempt(RawResponse? response, TypeSafeException? error, bool retryable, string reason, double? serverDelayMs)
        {
            Response = response;
            Error = error;
            Retryable = retryable;
            Reason = reason;
            ServerDelayMs = serverDelayMs;
        }

        public RawResponse? Response { get; }

        public TypeSafeException? Error { get; }

        public bool Retryable { get; }

        // The status code, "timeout" or "connection error", for logs.
        public string Reason { get; }

        public double? ServerDelayMs { get; }

        public static Attempt Succeeded(RawResponse response) => new(response, null, false, string.Empty, null);

        public static Attempt Failed(TypeSafeException error, bool retryable, string reason, double? serverDelayMs)
            => new(null, error, retryable, reason, serverDelayMs);
    }
}
