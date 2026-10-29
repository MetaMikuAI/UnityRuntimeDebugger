using System.Net;
using System.Net.Sockets;

namespace UnityRuntimeDebugger.Http;

/// <summary>
/// Minimal HTTP/1.1 server on a raw socket (one request per connection). HttpListener needs
/// http.sys on Windows and is not available on every mobile runtime; this works everywhere.
/// </summary>
internal sealed class HttpServer : IDisposable
{
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(15);

    private readonly TcpListener _listener;
    private readonly Func<HttpRequest, CancellationToken, Task<HttpResponse>> _handler;
    private readonly Action<string>? _onError;
    private readonly CancellationTokenSource _stopping = new();

    public HttpServer(IPAddress address, int port, Func<HttpRequest, CancellationToken, Task<HttpResponse>> handler, Action<string>? onError)
    {
        _listener = new TcpListener(address, port);
        _handler = handler;
        _onError = onError;
    }

    /// <summary>The bound endpoint; with port 0 the system picks one.</summary>
    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoop()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex)
            {
                _onError?.Invoke("accept failed: " + ex.Message);
                continue;
            }

            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();

                HttpRequest? request;
                using (var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token))
                {
                    headerTimeout.CancelAfter(HeaderTimeout);
                    request = await HttpRequest.ReadAsync(stream, headerTimeout.Token);
                }

                if (request == null)
                    return;

                HttpResponse response;
                try
                {
                    response = await _handler(request, _stopping.Token);
                }
                catch (ApiException ex)
                {
                    response = HttpResponse.Json(ex.ToBody(), ex.Status);
                }

                await response.WriteAsync(stream, request, _stopping.Token);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                // client went away or the server is stopping
            }
            catch (Exception ex)
            {
                _onError?.Invoke("request failed: " + ex);
            }
        }
    }
}
