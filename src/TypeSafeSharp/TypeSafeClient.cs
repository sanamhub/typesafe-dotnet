using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TypeSafeSharp;

/// <summary>
/// Client for TypeSafe AI's System One API. Thread-safe: create one per application and share it.
/// </summary>
public class TypeSafeClient : IDisposable
{
    private const string SystemOnePath = "/v1/systemone";
    private const string SystemOneEndpoint = "POST " + SystemOnePath;

    private readonly Transport? _transport;
    private readonly HttpClient? _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ModelsClient? _models;

    // Unknown answer kinds already logged, so a new kind warns once per client, not once per call.
    private readonly ConcurrentDictionary<string, bool> _reportedKinds = new(StringComparer.Ordinal);

    /// <summary>For mocking frameworks only. Every member of an instance made this way throws until a subclass overrides it.</summary>
    protected TypeSafeClient() { }

    /// <summary>Creates a client with an API key and default settings.</summary>
    /// <param name="apiKey">The API key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="apiKey"/> is null.</exception>
    /// <exception cref="TypeSafeConfigurationException">The key is empty or malformed, the environment's base URL is invalid, or the code runs in a browser.</exception>
    public TypeSafeClient(string apiKey)
        : this(new TypeSafeClientOptions { ApiKey = Guard.NotNull(apiKey) })
    {
    }

    /// <summary>Creates a client that owns its own <see cref="HttpClient"/>, disposed with the client.</summary>
    /// <param name="options">The settings. Copied, so later changes do not affect this client.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A timeout is not positive and finite, or <see cref="RetryPolicy.MaxRetries"/> is negative.</exception>
    /// <exception cref="TypeSafeConfigurationException">No API key was found, it is malformed, the base URL is invalid, or the code runs in a browser.</exception>
    public TypeSafeClient(TypeSafeClientOptions options)
        : this(Validate(options), httpClient: null, ownsHttpClient: true, TimeProvider.System, Jitter.Next)
    {
    }

    /// <summary>
    /// Creates a client on a caller's <see cref="HttpClient"/>, for extra handlers or headers. The
    /// client is never disposed or changed; its <c>DefaultRequestHeaders</c> apply where the SDK sets no header of that name.
    /// </summary>
    /// <param name="httpClient">The HTTP client to send with.</param>
    /// <param name="options">The settings. Copied, so later changes do not affect this client.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A timeout is not positive and finite, or <see cref="RetryPolicy.MaxRetries"/> is negative.</exception>
    /// <exception cref="TypeSafeConfigurationException">No API key was found, it is malformed, the base URL is invalid, or the code runs in a browser.</exception>
    public TypeSafeClient(HttpClient httpClient, TypeSafeClientOptions options)
        : this(httpClient, ownsHttpClient: false, options, TimeProvider.System, Jitter.Next)
    {
    }

    // The seam tests use, through InternalsVisibleTo.
    internal TypeSafeClient(HttpClient httpClient, bool ownsHttpClient, TypeSafeClientOptions options, TimeProvider timeProvider, Func<double> jitter)
        : this(Validate(options), Guard.NotNull(httpClient), ownsHttpClient, timeProvider, jitter)
    {
    }

    // Settings are built before any HttpClient exists, so invalid options cannot leak one.
    private TypeSafeClient(ClientSettings settings, HttpClient? httpClient, bool ownsHttpClient, TimeProvider timeProvider, Func<double> jitter)
    {
        _httpClient = httpClient ?? HttpDefaults.CreateClient();
        _ownsHttpClient = ownsHttpClient;
        _transport = new Transport(_httpClient, settings, timeProvider, jitter);
        _models = new ModelsClient(_transport);
    }

    /// <summary>The <c>/v1/models</c> endpoint.</summary>
    /// <exception cref="InvalidOperationException">The instance was made with the protected constructor and a subclass did not override this.</exception>
    public virtual ModelsClient Models => _models ?? throw MockOnly();

    /// <summary>Asks System One the request's questions about its state.</summary>
    /// <param name="request">The state and questions.</param>
    /// <param name="cancellationToken">Cancels the call, including retry waits.</param>
    /// <returns>The answers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException"><see cref="SystemOneRequest.ExtraBody"/> sets <c>state</c>, <c>model</c> or <c>questions</c>.</exception>
    /// <exception cref="TypeSafeApiException">The server returned a non-2xx status after retries; a subclass names the common statuses.</exception>
    /// <exception cref="TypeSafeTimeoutException">An attempt timed out after retries, or the total timeout ran out.</exception>
    /// <exception cref="TypeSafeConnectionException">No response arrived after retries.</exception>
    /// <exception cref="TypeSafeResponseValidationException">The server returned 2xx with a body that is not the documented response.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client was disposed.</exception>
    /// <exception cref="InvalidOperationException">The instance was made with the protected constructor.</exception>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "The overload shapes in PLAN.md section 3.1 were checked with the compiler: each call form binds to exactly one overload.")]
    public Task<SystemOneResponse> SystemOneAsync(SystemOneRequest request, CancellationToken cancellationToken = default)
        => SystemOneAsync(request, null, cancellationToken);

    /// <summary>Asks the questions about the state, with the client's default model.</summary>
    /// <param name="state">What to judge: text, or any JSON value.</param>
    /// <param name="questions">Question id to question, at least one.</param>
    /// <param name="cancellationToken">Cancels the call, including retry waits.</param>
    /// <returns>The answers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="questions"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="questions"/> is empty, or has a null or empty id or a null question.</exception>
    /// <exception cref="TypeSafeApiException">The server returned a non-2xx status after retries; a subclass names the common statuses.</exception>
    /// <exception cref="TypeSafeTimeoutException">An attempt timed out after retries, or the total timeout ran out.</exception>
    /// <exception cref="TypeSafeConnectionException">No response arrived after retries.</exception>
    /// <exception cref="TypeSafeResponseValidationException">The server returned 2xx with a body that is not the documented response.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client was disposed.</exception>
    /// <exception cref="InvalidOperationException">The instance was made with the protected constructor.</exception>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "The overload shapes in PLAN.md section 3.1 were checked with the compiler: each call form binds to exactly one overload.")]
    public Task<SystemOneResponse> SystemOneAsync(JsonNode state, IReadOnlyDictionary<string, Question> questions, CancellationToken cancellationToken = default)
        => SystemOneAsync(new SystemOneRequest(state, questions), null, cancellationToken);

    /// <summary>Asks System One the request's questions, with per-call timeouts or retries.</summary>
    /// <param name="request">The state and questions.</param>
    /// <param name="options">Overrides for this call, or null for the client's settings.</param>
    /// <param name="cancellationToken">Cancels the call, including retry waits.</param>
    /// <returns>The answers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException"><see cref="SystemOneRequest.ExtraBody"/> sets <c>state</c>, <c>model</c> or <c>questions</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A timeout in <paramref name="options"/> is not positive and finite, or its <see cref="RetryPolicy.MaxRetries"/> is negative.</exception>
    /// <exception cref="TypeSafeApiException">The server returned a non-2xx status after retries; a subclass names the common statuses.</exception>
    /// <exception cref="TypeSafeTimeoutException">An attempt timed out after retries, or the total timeout ran out.</exception>
    /// <exception cref="TypeSafeConnectionException">No response arrived after retries.</exception>
    /// <exception cref="TypeSafeResponseValidationException">The server returned 2xx with a body that is not the documented response.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client was disposed.</exception>
    /// <exception cref="InvalidOperationException">The instance was made with the protected constructor and a subclass did not override this.</exception>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "The overload shapes in PLAN.md section 3.1 were checked with the compiler: each call form binds to exactly one overload.")]
    public virtual async Task<SystemOneResponse> SystemOneAsync(SystemOneRequest request, TypeSafeRequestOptions? options, CancellationToken cancellationToken = default)
    {
        var transport = _transport ?? throw MockOnly();
        Guard.NotNull(request);
        var call = CallSettings.Resolve(transport.Settings, options, SystemOneEndpoint, request.Model ?? transport.Settings.DefaultModel);
        using var activity = Telemetry.StartSystemOne(call.Model!, transport.Settings.BaseUrl);
        try
        {
            var body = RequestWriter.Write(request, call.Model!);
            var raw = await transport.SendAsync(HttpMethod.Post, SystemOnePath, body, call, cancellationToken).ConfigureAwait(false);
            var response = ResponseReader.ReadSystemOne(raw.Body, raw.RequestId);
            ReportUnknownKinds(transport, response);
            Telemetry.Succeeded(activity, response, raw.StatusCode);
            return response;
        }
        catch (Exception ex)
        {
            Telemetry.Failed(activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Evaluates many items with up to 4 calls at once, yielding each result as it completes.
    /// Arguments are checked here, not when enumeration starts.
    /// </summary>
    /// <typeparam name="TItem">The caller's item type.</typeparam>
    /// <param name="items">The items, read lazily as slots free up.</param>
    /// <param name="createRequest">Builds the request for one item. An <see cref="ArgumentException"/> it throws fails that item only.</param>
    /// <param name="cancellationToken">Stops the enumeration and cancels the calls in flight.</param>
    /// <returns>One <see cref="BatchItem{TItem}"/> per item, in completion order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="createRequest"/> is null.</exception>
    /// <exception cref="TypeSafeAuthenticationException">Thrown while enumerating: the key was rejected. Ends the batch, since every item would fail the same way.</exception>
    /// <exception cref="TypeSafePermissionDeniedException">Thrown while enumerating: the key may not use this model or endpoint. Ends the batch.</exception>
    /// <exception cref="TypeSafeNotFoundException">Thrown while enumerating: usually a wrong model or base URL. Ends the batch.</exception>
    /// <exception cref="TypeSafeConfigurationException">Thrown while enumerating: the client cannot send at all. Ends the batch.</exception>
    /// <exception cref="OperationCanceledException">Thrown while enumerating: <paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown while enumerating: the client was disposed.</exception>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "The overload shapes in PLAN.md section 3.1 were checked with the compiler: each call form binds to exactly one overload.")]
    public IAsyncEnumerable<BatchItem<TItem>> EvaluateManyAsync<TItem>(IEnumerable<TItem> items, Func<TItem, SystemOneRequest> createRequest, CancellationToken cancellationToken = default)
        => EvaluateManyAsync(items, createRequest, null, cancellationToken);

    /// <summary>
    /// Evaluates many items with bounded concurrency, yielding each result as it completes.
    /// Arguments are checked here, not when enumeration starts.
    /// </summary>
    /// <typeparam name="TItem">The caller's item type.</typeparam>
    /// <param name="items">The items, read lazily as slots free up.</param>
    /// <param name="createRequest">Builds the request for one item. An <see cref="ArgumentException"/> it throws fails that item only.</param>
    /// <param name="options">Concurrency and per-call options, or null for the defaults.</param>
    /// <param name="cancellationToken">Stops the enumeration and cancels the calls in flight.</param>
    /// <returns>One <see cref="BatchItem{TItem}"/> per item, in completion order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="createRequest"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="BatchOptions.MaxConcurrency"/> is less than 1.</exception>
    /// <exception cref="TypeSafeAuthenticationException">Thrown while enumerating: the key was rejected. Ends the batch, since every item would fail the same way.</exception>
    /// <exception cref="TypeSafePermissionDeniedException">Thrown while enumerating: the key may not use this model or endpoint. Ends the batch.</exception>
    /// <exception cref="TypeSafeNotFoundException">Thrown while enumerating: usually a wrong model or base URL. Ends the batch.</exception>
    /// <exception cref="TypeSafeConfigurationException">Thrown while enumerating: the client cannot send at all. Ends the batch.</exception>
    /// <exception cref="OperationCanceledException">Thrown while enumerating: <paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown while enumerating: the client was disposed.</exception>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "The overload shapes in PLAN.md section 3.1 were checked with the compiler: each call form binds to exactly one overload.")]
    public IAsyncEnumerable<BatchItem<TItem>> EvaluateManyAsync<TItem>(IEnumerable<TItem> items, Func<TItem, SystemOneRequest> createRequest, BatchOptions? options, CancellationToken cancellationToken = default)
        => BatchRunner.Run(this, items, createRequest, options, _transport?.Time ?? TimeProvider.System, cancellationToken);

    /// <summary>Stops the client. Calls in flight and later calls throw <see cref="ObjectDisposedException"/>. Disposes the <see cref="HttpClient"/> only if this client created it.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Swapped only by tests (AC-3.15), which run in the non-parallel environment collection.
    internal static Func<bool> IsBrowser { get; set; } = static () => RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER"));

    internal static void ThrowIfBrowser(bool isBrowser)
    {
        // As the JS SDK's refuseBrowser: the key would ship to every visitor.
        if (isBrowser)
        {
            throw new TypeSafeConfigurationException("TypeSafeClient runs server-side only; a browser app would expose the API key.");
        }
    }

    /// <summary>Releases the client's resources.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        // Flag first, so an attempt cut short by HttpClient.Dispose reports disposal, not a timeout.
        _transport?.MarkDisposed();
        if (_ownsHttpClient)
        {
            _httpClient?.Dispose();
        }
    }

    private void ReportUnknownKinds(Transport transport, SystemOneResponse response)
    {
        foreach (var pair in response.Answers)
        {
            if (pair.Value is UnknownAnswer unknown && _reportedKinds.TryAdd(unknown.Type, true))
            {
                Log.UnknownAnswerKind(transport.Logger, unknown.Type, pair.Key);
            }
        }
    }

    private static ClientSettings Validate(TypeSafeClientOptions options)
    {
        ThrowIfBrowser(IsBrowser());
        return ClientSettings.From(Guard.NotNull(options));
    }

    private static InvalidOperationException MockOnly()
        => new("This TypeSafeClient was created with the protected constructor for mocking; override the member you call.");
}
