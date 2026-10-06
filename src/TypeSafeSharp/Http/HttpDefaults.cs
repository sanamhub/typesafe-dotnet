using System;
using System.Net.Http;
using System.Threading;

namespace TypeSafeSharp;

internal static class HttpDefaults
{
    public static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    // The one #if NET in src (ADR-0001): which handler, never what the client does with it.
    // No redirects: following one would turn the POST into a GET and hide the 3xx from the caller.
    public static HttpMessageHandler CreateHandler()
    {
#if NET
        return new SocketsHttpHandler { PooledConnectionLifetime = PooledConnectionLifetime, AllowAutoRedirect = false };
#else
        // .NET Framework allows 2 connections per host by default, which would queue a batch of 4
        // and burn its attempt timeout waiting.
        return new HttpClientHandler { MaxConnectionsPerServer = 64, AllowAutoRedirect = false };
#endif
    }

    // HTTP/2 lets a batch share one connection; OrLower keeps 1.1-only proxies working.
    // .NET Framework's handler throws on 2.0, so ns2.0 leaves the 1.1 default.
    public static void SetVersion(HttpRequestMessage request)
    {
#if NET
        request.Version = System.Net.HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
#endif
    }

#pragma warning disable CA2000 // The HttpClient owns the handler (disposeHandler: true).
    // SDK timers own timeouts, so HttpClient's own 100 s default must not fire first.
    public static HttpClient CreateClient() => new(CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
#pragma warning restore CA2000
}
