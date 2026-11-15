using System.Diagnostics;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// An extract's download (T127): a line that goes dead part-way, with nothing more coming and
/// nothing closed (the laptop asleep), is given up after a while rather than waited on for ever.
/// What was there before stays as it was.
/// </summary>
public class DownloadingAnExtract
{
    [Test]
    public async Task A_download_that_stalls_is_given_up_and_changes_nothing()
    {
        using var server = new ExtractServer("stalling", new byte[200_000], DateTimeOffset.UnixEpoch) { StallAfter = 1_000 };
        var folder = Directory.CreateTempSubdirectory("cutthrough-stall-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "stalling-latest.osm"), "before");
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var clock = Stopwatch.StartNew();
            var stalled = await Assert.ThrowsAsync<ExtractNotDownloadedException>(() =>
                new ExtractSource(http, server.ExtractUrl).DownloadAsync(folder, stall: TimeSpan.FromSeconds(1)));
            Assert.That((stalled!.Message.Contains("stalled", StringComparison.Ordinal), clock.Elapsed < TimeSpan.FromSeconds(30),
                    await File.ReadAllTextAsync(Path.Combine(folder, "stalling-latest.osm")), File.Exists(Path.Combine(folder, "stalling-latest.osm.part"))),
                Is.EqualTo((true, true, "before", false)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    //  Slow but steady (a thousand bytes every 0.3 s, three seconds in all): not given up after the
    //  second it may go quiet, as each byte that comes puts the wait back.
    [Test]
    public async Task A_slow_but_steady_download_isnt_given_up()
    {
        var extract = new byte[10_000];
        Random.Shared.NextBytes(extract);
        using var server = new ExtractServer("trickling", extract, DateTimeOffset.UnixEpoch) { Trickle = TimeSpan.FromSeconds(0.3) };
        var folder = Directory.CreateTempSubdirectory("cutthrough-trickle-").FullName;
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var kept = await new ExtractSource(http, server.ExtractUrl).DownloadAsync(folder, stall: TimeSpan.FromSeconds(1));
            Assert.That(await File.ReadAllBytesAsync(kept), Is.EqualTo(extract));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
