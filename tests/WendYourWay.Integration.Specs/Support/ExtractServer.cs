using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WendYourWay.Integration.Specs;

/// <summary>
/// A stand-in for Geofabrik (T127), on a loopback port: an extract at `/{name}-latest.osm`, its
/// `.md5` beside it, and `/{name}-updates/state.txt` saying when its data runs to, as Geofabrik's
/// does (its colons escaped). Each answer can be set wrong, and what was asked for is kept.
/// </summary>
internal sealed class ExtractServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public ExtractServer(string name, byte[] extract, DateTimeOffset dataAt)
    {
        (Name, Extract, DataAt) = (name, extract, dataAt);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public string Name { get; }

    /// <summary>The extract served.</summary>
    public byte[] Extract { get; set; }

    /// <summary>When the extract's data runs to, as `state.txt` says.</summary>
    public DateTimeOffset DataAt { get; set; }

    /// <summary>Set, the `.md5` says this instead of the extract's own.</summary>
    public string? WrongMd5 { get; set; }

    /// <summary>Set, the extract stops after this many bytes, its length promised in full.</summary>
    public int? CutOffAfter { get; set; }

    /// <summary>Set, the extract stops after this many bytes and the connection is held open, as a dead line does.</summary>
    public int? StallAfter { get; set; }

    /// <summary>Set, the extract comes a thousand bytes at a time, this long apart: slow but steady.</summary>
    public TimeSpan? Trickle { get; set; }

    /// <summary>Set, Geofabrik's index of its extracts (T143), at `/index-v1-nogeom.json`.</summary>
    public string? Index { get; set; }

    /// <summary>The index's address, as `wend network build --index` takes it.</summary>
    public string IndexUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/index-v1-nogeom.json";

    /// <summary>Every path asked for, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>The extract's address, as `wend update --source` takes it.</summary>
    public string ExtractUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{(Folders ? $"/{Name}/{Name}-latest.osm" : $"/{Name}-latest.osm")}";

    /// <summary>
    /// Set, laid out as Geofabrik has been since November 2026 (T151): a folder each, the extract at
    /// `/{name}/{name}-latest.osm` sent on (307) to the day's file, `/{name}/{name}-261118.osm`, its
    /// `.md5` beside that only, and the date in `/{name}/updates/state.txt`.
    /// </summary>
    public bool Folders { get; init; }

    /// <summary>
    /// Set, the day's `.md5` isn't found, as Geofabrik's wasn't on 22 November 2026 (#155), though the
    /// folder's `-latest` one, which it now keeps too, is.
    /// </summary>
    public bool DaysMd5Missing { get; init; }

    /// <summary>
    /// Set, the day's file the extract is sent on to answers this instead (404, then 503, from Geofabrik on
    /// 23 November 2026, T184), though the day before's, with its MD5, is there.
    /// </summary>
    public int? DaysFileAnswers { get; init; }

    private bool DaysFileMissing => DaysFileAnswers is not null;

    // Where the extract is sent on to: the day's file.
    private string Sent => $"/{Name}/{Name}-261118.osm";

    // Where the extract's bytes are, and its date.
    private string Served => !Folders ? $"/{Name}-latest.osm" : DaysFileMissing ? $"/{Name}/{Name}-261117.osm" : Sent;

    private string State => Folders ? $"/{Name}/updates/state.txt" : $"/{Name}-updates/state.txt";

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
            var request = await reader.ReadLineAsync();
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            var path = request?.Split(' ')[1] ?? "/";
            lock (Asked) Asked.Add(path);

            // MD5 because Geofabrik publishes it: a check the file arrived whole, not of who sent it.
#pragma warning disable CA5351
            var (status, body, promised) = path switch
            {
                _ when Folders && path == $"/{Name}/{Name}-latest.osm" => (307, Array.Empty<byte>(), (int?)null),
                _ when path == State => (200, Encoding.ASCII.GetBytes(
                    $"#Wed Nov 11 21:30:00 UTC 2026\nsequenceNumber=4960\ntimestamp={DataAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH\\\\:mm\\\\:ss'Z'", CultureInfo.InvariantCulture)}\n"), (int?)null),
                _ when DaysMd5Missing && path == Served + ".md5" => (404, Encoding.ASCII.GetBytes("Not found"), (int?)null),
                _ when (path == Served + ".md5" || (Folders && path == $"/{Name}/{Name}-latest.osm.md5")) => (200, Encoding.ASCII.GetBytes($"{WrongMd5 ?? Convert.ToHexStringLower(MD5.HashData(Extract))}  {Name}-latest.osm\n"), null),
                _ when path == "/index-v1-nogeom.json" && Index is not null => (200, Encoding.UTF8.GetBytes(Index), null),
                _ when path == Served => (200, CutOffAfter is { } cut ? Extract[..cut] : Extract, Extract.Length),
                _ when DaysFileMissing && path == Sent => (DaysFileAnswers!.Value, Encoding.ASCII.GetBytes("Not here yet"), (int?)null),
                _ => (404, Encoding.ASCII.GetBytes("Not found"), null),
            };
#pragma warning restore CA5351
            var head = $"HTTP/1.1 {status} {status switch { 200 => "OK", 307 => "Temporary Redirect", 503 => "Service Unavailable", _ => "Not Found" }}\r\n"
                + (status == 307 ? $"Location: {Sent}\r\n" : "")
                + $"Content-Length: {promised ?? body.Length}\r\nConnection: close\r\n\r\n";
            try
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                if (StallAfter is { } stall && path == Served)
                {
                    await stream.WriteAsync(body.AsMemory(0, stall));
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                }
                if (Trickle is { } every && path == Served)
                {
                    for (var at = 0; at < body.Length; at += 1_000)
                    {
                        await stream.WriteAsync(body.AsMemory(at, Math.Min(1_000, body.Length - at)));
                        await stream.FlushAsync();
                        await Task.Delay(every, _stop.Token);
                    }
                    return;
                }
                await stream.WriteAsync(body);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException)
            {
                // The client gave up, or the server stopped.
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
