// C# 7.3, because the NetFx consumer compiles this file too.
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Consumer
{
    // A loopback stand-in for api.typesafe.ai: POST /v1/systemone gets the canned answer, anything
    // else a 404. Plain http is allowed for loopback addresses only (ClientSettings).
    internal sealed class StubServer : IDisposable
    {
        public const string RequestId = "req_test_0001";

        // noul.json from the fixtures plus a choice answer, so both question kinds are read.
        private const string Body =
            "{\"model\":\"jev-1.13.0\",\"answers\":{" +
            "\"is_urgent\":{\"type\":\"noul\",\"noul\":0.95}," +
            "\"department\":{\"type\":\"choice\",\"choice\":\"billing\",\"probabilities\":{\"billing\":0.88,\"technical\":0.12},\"confidence\":0.81}}," +
            "\"usage\":{\"input_tokens\":296,\"output_tokens\":20}}";

        private readonly HttpListener _listener = new HttpListener();

        public StubServer()
        {
            BaseUrl = new Uri("http://localhost:" + FreePort() + "/");
            LastAuthorization = string.Empty;
            _listener.Prefixes.Add(BaseUrl.AbsoluteUri);
            _listener.Start();
            Task.Run(ServeAsync);
        }

        public Uri BaseUrl { get; }

        public string LastAuthorization { get; private set; }

        public void Dispose() => _listener.Close();

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                var response = context.Response;
                if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/v1/systemone")
                {
                    LastAuthorization = context.Request.Headers["Authorization"] ?? string.Empty;
                    var bytes = Encoding.UTF8.GetBytes(Body);
                    response.StatusCode = 200;
                    response.ContentType = "application/json";
                    response.Headers["x-typesafe-request-id"] = RequestId;
                    response.ContentLength64 = bytes.Length;
                    await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                }
                else
                {
                    response.StatusCode = 404;
                }

                response.Close();
            }
        }
    }
}
