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

    //  Geofabrik's folders (#155, T151): the extract sent on to the day's file, whose MD5 is beside
    //  it only, and the date in the folder's updates.
    [Test]
    public async Task From_Geofabriks_folders_the_days_file_is_downloaded_and_checked_against_its_own_MD5()
    {
        var extract = new byte[10_000];
        Random.Shared.NextBytes(extract);
        using var server = new ExtractServer("foldered", extract, DateTimeOffset.UnixEpoch) { Folders = true };
        var folder = Directory.CreateTempSubdirectory("cutthrough-folders-").FullName;
        try
        {
            using var http = new HttpClient();
            var kept = await new ExtractSource(http, server.ExtractUrl).DownloadAsync(folder);
            Assert.That((Path.GetFileName(kept), await File.ReadAllBytesAsync(kept)), Is.EqualTo(("foldered-latest.osm", extract)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    //  Found on 22 November 2026 (#155): the day's MD5 not found, so CI's build failed. The folder's
    //  `-latest` MD5, which Geofabrik now keeps too, checks it instead.
    [Test]
    public async Task Where_the_days_MD5_isnt_found_the_folders_latest_one_checks_it()
    {
        var extract = new byte[10_000];
        Random.Shared.NextBytes(extract);
        using var server = new ExtractServer("foldered", extract, DateTimeOffset.UnixEpoch) { Folders = true, DaysMd5Missing = true };
        var folder = Directory.CreateTempSubdirectory("cutthrough-folders-").FullName;
        try
        {
            using var http = new HttpClient();
            var kept = await new ExtractSource(http, server.ExtractUrl).DownloadAsync(folder);
            Assert.That(await File.ReadAllBytesAsync(kept), Is.EqualTo(extract));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    //  Found on 23 November 2026 (T184, #188): the day's file the extract is sent on to not found, so CI's
    //  build failed again. The day before's is taken, checked against its own MD5, and said.
    [TestCase(404, Description = "not found")]
    [TestCase(503, Description = "not ready: Geofabrik part-way through making it")]
    public async Task Where_the_days_file_isnt_there_the_day_befores_is_taken_and_said(int answer)
    {
        var extract = new byte[10_000];
        Random.Shared.NextBytes(extract);
        using var server = new ExtractServer("foldered", extract, DateTimeOffset.UnixEpoch) { Folders = true, DaysFileAnswers = answer };
        var folder = Directory.CreateTempSubdirectory("cutthrough-folders-").FullName;
        try
        {
            using var http = new HttpClient();
            var source = new ExtractSource(http, server.ExtractUrl);
            var kept = await source.DownloadAsync(folder);
            Assert.That((await File.ReadAllBytesAsync(kept), source.TookEarlier), Is.EqualTo((extract, (DateOnly?)new DateOnly(2026, 11, 17))));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    //  On the next try that day (T184), the day's file didn't answer at all. After a while the day before's is
    //  taken, named from the date the folder's updates give.
    [Test]
    public async Task Where_the_days_file_never_answers_the_day_befores_is_taken_after_a_while()
    {
        var extract = new byte[10_000];
        Random.Shared.NextBytes(extract);
        using var server = new ExtractServer("foldered", extract, new DateTimeOffset(2026, 11, 18, 21, 21, 2, TimeSpan.Zero)) { Folders = true, DaysFileHangs = true };
        var folder = Directory.CreateTempSubdirectory("cutthrough-folders-").FullName;
        try
        {
            using var http = new HttpClient();
            var source = new ExtractSource(http, server.ExtractUrl) { DaysFileWait = TimeSpan.FromSeconds(1) };
            var kept = await source.DownloadAsync(folder);
            Assert.That((await File.ReadAllBytesAsync(kept), source.TookEarlier), Is.EqualTo((extract, (DateOnly?)new DateOnly(2026, 11, 17))));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void An_earlier_days_file_taken_is_said_by_its_day() =>
        Assert.That(ExtractSource.Earlier(new DateOnly(2026, 11, 21)), Is.EqualTo("  Geofabrik's newest file isn't there yet: using 21 Nov's instead"));

    //  The day's file found: nothing earlier taken.
    [Test]
    public async Task Where_the_days_file_is_found_nothing_earlier_is_said()
    {
        using var server = new ExtractServer("foldered", new byte[1_000], DateTimeOffset.UnixEpoch) { Folders = true };
        var folder = Directory.CreateTempSubdirectory("cutthrough-folders-").FullName;
        try
        {
            using var http = new HttpClient();
            var source = new ExtractSource(http, server.ExtractUrl);
            await source.DownloadAsync(folder);
            Assert.That(source.TookEarlier, Is.Null);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public async Task From_Geofabriks_folders_the_date_is_read_from_the_folders_updates()
    {
        var at = new DateTimeOffset(2026, 11, 18, 21, 21, 2, TimeSpan.Zero);
        using var server = new ExtractServer("foldered", [], at) { Folders = true };
        using var http = new HttpClient();
        Assert.That(await new ExtractSource(http, server.ExtractUrl).DataAtAsync(), Is.EqualTo(at));
    }

    [Test]
    public void Great_Britain_is_asked_for_in_its_folder() =>
        Assert.That(ExtractSource.GreatBritain, Is.EqualTo("https://download.geofabrik.de/europe/great-britain/great-britain-latest.osm.pbf"));

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
