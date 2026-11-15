using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WendYourWay.Integration.Specs;

/// <summary>
/// A stand-in website on a loopback port (T142): each path answers with the text set for it, a HEAD
/// with its length; anything else, 404. What was asked for is kept, with its method.
/// </summary>
internal sealed class StubSite : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public StubSite()
    {
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    /// <summary>What each path answers.</summary>
    public Dictionary<string, string> Pages { get; } = [];

    /// <summary>Every request, as "GET /path" or "HEAD /path", in order.</summary>
    public List<string> Asked { get; } = [];

    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Task.Run(() => AnswerAsync(client));
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var request = (await reader.ReadLineAsync())?.Split(' ') ?? [];
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            var (method, path) = request.Length > 1 ? (request[0], request[1]) : ("GET", "/");
            lock (Asked) Asked.Add($"{method} {path}");
            var found = Pages.TryGetValue(path, out var page);
            var body = Encoding.UTF8.GetBytes(found ? page! : "Not found");
            var head = $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            try
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                if (method != "HEAD") await stream.WriteAsync(body);
            }
            catch (IOException)
            {
                // The client gave up.
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}
