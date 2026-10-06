using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TypeSafeSharp;

/// <summary>The <c>/v1/models</c> endpoint. Get it from <see cref="TypeSafeClient.Models"/>.</summary>
public class ModelsClient
{
    private const string ModelsPath = "/v1/models";

    private readonly Transport? _transport;

    /// <summary>For mocking frameworks only. <see cref="ListAsync"/> throws until a subclass overrides it.</summary>
    protected ModelsClient() { }

    internal ModelsClient(Transport transport) => _transport = transport;

    /// <summary>Lists the models the API key can use.</summary>
    /// <param name="cancellationToken">Cancels the call, including retry waits.</param>
    /// <returns>The models, in the order the server sent them.</returns>
    /// <exception cref="TypeSafeApiException">The server returned a non-2xx status after retries; a subclass names the common statuses.</exception>
    /// <exception cref="TypeSafeTimeoutException">An attempt timed out after retries, or the total timeout ran out.</exception>
    /// <exception cref="TypeSafeConnectionException">No response arrived after retries.</exception>
    /// <exception cref="TypeSafeResponseValidationException">The server returned 2xx with a body that is not the documented response.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client was disposed.</exception>
    /// <exception cref="InvalidOperationException">The instance was made with the protected constructor and a subclass did not override this.</exception>
    public virtual async Task<IReadOnlyList<ModelCard>> ListAsync(CancellationToken cancellationToken = default)
    {
        var transport = _transport
            ?? throw new InvalidOperationException("This ModelsClient was created with the protected constructor for mocking; override ListAsync.");
        var call = CallSettings.Resolve(transport.Settings, null, "GET " + ModelsPath, null);
        var response = await transport.SendAsync(HttpMethod.Get, ModelsPath, null, call, cancellationToken).ConfigureAwait(false);
        return ResponseReader.ReadModels(response.Body);
    }
}
