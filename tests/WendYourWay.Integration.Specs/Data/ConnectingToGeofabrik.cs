using System.Net;
using System.Net.Sockets;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Geofabrik reached (#158, T154): its host answers on IPv6 as well as IPv4, and a machine with no
/// IPv6 (GitHub's runners) was told "Network is unreachable". Its IPv4 addresses are tried first,
/// then its IPv6 ones, each in turn; none answering, all are tried again a few times.
/// </summary>
public class ConnectingToGeofabrik
{
    private static readonly IPAddress SixA = IPAddress.Parse("2a01:4f9:2b:27d0::2");
    private static readonly IPAddress SixB = IPAddress.Parse("2a01:4f9:2b:2820::2");
    private static readonly IPAddress FourA = IPAddress.Parse("95.216.245.233");
    private static readonly IPAddress FourB = IPAddress.Parse("95.217.45.61");

    private readonly List<IPAddress> _tried = [];
    private readonly List<TimeSpan> _waited = [];

    [SetUp]
    public void Arrange()
    {
        _tried.Clear();
        _waited.Clear();
    }

    private Task<Stream> Connect(IPAddress[] found, Func<IPAddress, bool> answers) =>
        GeofabrikConnection.ConnectAsync("download.geofabrik.de", 443,
            (_, _) => Task.FromResult(found),
            (address, _, _) =>
            {
                _tried.Add(address);
                return answers(address) ? Task.FromResult<Stream>(new MemoryStream()) : Task.FromException<Stream>(new SocketException((int)SocketError.NetworkUnreachable));
            },
            (wait, _) =>
            {
                _waited.Add(wait);
                return Task.CompletedTask;
            },
            CancellationToken.None);

    [Test]
    public async Task Its_IPv4_addresses_are_tried_before_its_IPv6_ones()
    {
        await Connect([SixA, FourA, SixB, FourB], a => a.Equals(SixB));
        Assert.That(_tried, Is.EqualTo(new[] { FourA, FourB, SixA, SixB }));
    }

    [Test]
    public async Task The_first_that_answers_is_kept()
    {
        await Connect([SixA, FourA, SixB, FourB], _ => true);
        Assert.That((_tried.Single(), _waited.Count), Is.EqualTo((FourA, 0)));
    }

    [Test]
    public async Task None_answering_all_are_tried_again_a_few_times_then_it_says_why()
    {
        var failed = await Assert.ThrowsAsync<SocketException>(() => Connect([SixA, FourA], _ => false));
        Assert.That((_tried.Count, _waited.Count, failed!.SocketErrorCode),
            Is.EqualTo((2 * GeofabrikConnection.Rounds, GeofabrikConnection.Rounds - 1, SocketError.NetworkUnreachable)));
    }

    //  The client made with it downloads, as the region download and the map update use it.
    [Test]
    public async Task Its_client_downloads_an_extract()
    {
        var extract = new byte[5_000];
        Random.Shared.NextBytes(extract);
        using var server = new ExtractServer("connecting", extract, DateTimeOffset.UnixEpoch);
        var folder = Directory.CreateTempSubdirectory("cutthrough-connect-").FullName;
        try
        {
            using var http = GeofabrikConnection.Client("WendYourWay-specs/1.0");
            var file = await new ExtractSource(http, server.ExtractUrl).DownloadAsync(folder);
            Assert.That(await File.ReadAllBytesAsync(file), Is.EqualTo(extract));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
