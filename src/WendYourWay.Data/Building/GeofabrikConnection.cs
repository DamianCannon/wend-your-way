using System.Net;
using System.Net.Sockets;

namespace WendYourWay.Data.Building;

/// <summary>
/// How Geofabrik is reached (#158, T154): its host answers on IPv6 as well as IPv4 (since about
/// 18 Nov 2026), and a machine with no IPv6, as GitHub's runners are, was told "Network is
/// unreachable". Its IPv4 addresses are tried first, then its IPv6 ones, each in turn; none
/// answering, all are tried again a few times, a little longer apart.
/// </summary>
public static class GeofabrikConnection
{
    /// <summary>How many times every address is tried before giving up.</summary>
    public const int Rounds = 3;

    // Long enough for a slow line, short enough that a dead address doesn't hold up the next.
    private static readonly TimeSpan EachTry = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A client connecting this way: by default no timeout of its own, as a 2 GB download takes a
    /// while, and the run's token stops it.
    /// </summary>
    public static HttpClient Client(string userAgent, TimeSpan? timeout = null) => new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectCallback = (context, cancellationToken) => new ValueTask<Stream>(ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port,
            Dns.GetHostAddressesAsync, SocketAsync, Task.Delay, cancellationToken)),
    })
    {
        Timeout = timeout ?? Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", userAgent } },
    };

    /// <summary>
    /// A connection to the host: its IPv4 addresses first, then IPv6, the first that answers; none
    /// answering in <see cref="Rounds"/> tries of each, the last failure.
    /// </summary>
    public static async Task<Stream> ConnectAsync(
        string host, int port,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, int, CancellationToken, Task<Stream>> connect,
        Func<TimeSpan, CancellationToken, Task> wait,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(wait);
        Exception? last = null;
        for (var round = 1; round <= Rounds; round++)
        {
            if (round > 1) await wait(TimeSpan.FromSeconds(5 * (round - 1)), cancellationToken);
            IPAddress[] addresses;
            try
            {
                addresses = await resolve(host, cancellationToken);
            }
            catch (SocketException e)
            {
                last = e;
                continue;
            }
            foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
            {
                try
                {
                    return await connect(address, port, cancellationToken);
                }
                catch (Exception e) when (e is SocketException or IOException or TimeoutException)
                {
                    last = e;
                }
            }
        }
        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }

    // One address tried, for EachTry at most.
    private static async Task<Stream> SocketAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        using var trying = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        trying.CancelAfter(EachTry);
        try
        {
            await socket.ConnectAsync(address, port, trying.Token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TimeoutException($"No answer from {address} in {EachTry.TotalSeconds:0} s.");
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
