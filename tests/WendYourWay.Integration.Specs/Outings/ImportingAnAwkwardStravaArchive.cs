using System.IO.Compression;
using System.Text;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Integration.Specs.Support;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// Real archives hold awkward activities, and a long import can be stopped part-way (review after
/// T030). One bad file must not stop the rest, and an import run again must finish what the last
/// one left. On Fixtures/same-ground.osm, with invented recordings along road 200's north pavement.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ImportingAnAwkwardStravaArchive
{
    private static readonly System.DateTime Start = new(2024, 6, 1, 7, 0, 0, DateTimeKind.Utc);

    private NpgsqlDataSource _dataSource = null!;
    private StravaImport _import = null!;

    //  Run 301; a file whose times are empty (302); and 303, the very same recording as 301 under
    //  a second Strava id (uploaded twice, say).
    private static string Archive()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("cutthrough-awkward-").FullName, "export_awkward.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, byte[] content)
        {
            using var entry = zip.CreateEntry(name).Open();
            entry.Write(content);
        }
        Add("activities.csv", Encoding.UTF8.GetBytes("""
            Activity ID,Activity Date,Activity Name,Activity Type,Filename
            301,"Jun 1, 2024, 7:00:00 AM",Morning Run,Run,activities/301.gpx
            302,"Jun 2, 2024, 7:00:00 AM",Blank Times,Run,activities/302.gpx
            303,"Jun 1, 2024, 7:00:00 AM",Morning Run Again,Run,activities/303.gpx
            """.ReplaceLineEndings("\n")));
        var run = TrackFiles.Gpx(SampleStravaArchive.AlongThePavement(Start), "running");
        var blank = Encoding.UTF8.GetString(TrackFiles.Gpx(SampleStravaArchive.AlongThePavement(Start.AddDays(1)), "running"));
        Add("activities/301.gpx", run);
        Add("activities/302.gpx", Encoding.UTF8.GetBytes(System.Text.RegularExpressions.Regex.Replace(blank, "<time>[^<]*</time>", "<time></time>")));
        Add("activities/303.gpx", run);
        return path;
    }

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _import = await StravaImporter.ImportAsync(_dataSource, Archive());
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public void Then_a_file_it_can_t_read_is_listed_and_the_rest_are_imported() =>
        Assert.That((_import.Imported, _import.NotRead.Select(n => n.Activity).ToArray()), Is.EqualTo((1, new[] { "2024-06-02 Blank Times" })));

    [Test]
    public void Then_the_same_recording_under_a_second_Strava_id_is_already_there() =>
        Assert.That(_import.AlreadyThere, Is.EqualTo(1));
}

/// <summary>
/// An import stopped after its Outings were added but before they were covered (Ctrl+C, the
/// laptop sleeping) is finished by running it again. On Fixtures/same-ground.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ResumingAStravaImport
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);

        var archive = SampleStravaArchive.Build();
        await StravaImporter.ImportAsync(_dataSource, archive);
        //  As a stopped import leaves them: added, not covered.
        await using (var stop = _dataSource.CreateCommand(
            "delete from app.outing_coverage; delete from app.outing_gaps; update app.outings set coverage = null"))
        {
            await stop.ExecuteNonQueryAsync();
        }
        await StravaImporter.ImportAsync(_dataSource, archive);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public async Task Then_running_it_again_covers_every_Outing()
    {
        await using var query = _dataSource.CreateCommand("select count(*) from app.outings where coverage is null");
        Assert.That(await query.ExecuteScalarAsync(), Is.Zero);
    }
}

/// <summary>
/// Two runs imported from their own files start 70 s apart, so they're two Outings; an archive
/// activity starting 50 s after the first, within a minute of both, is the one it started
/// nearest (20 s), not whichever comes first. On Fixtures/same-ground.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MatchingAnArchiveActivityToTheNearestStart
{
    private static readonly System.DateTime Start = new(2024, 7, 1, 7, 0, 0, DateTimeKind.Utc);

    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);

        var folder = Directory.CreateTempSubdirectory("cutthrough-nearest-").FullName;
        foreach (var (name, start) in new[] { ("First_Watch.gpx", Start), ("Second_Watch.gpx", Start.AddSeconds(70)) })
        {
            var gpx = Path.Combine(folder, name);
            await System.IO.File.WriteAllBytesAsync(gpx, TrackFiles.Gpx(SampleStravaArchive.AlongThePavement(start), "running", name));
            await OutingImporter.ImportGpxAsync(_dataSource, gpx);
        }

        //  The archive's original of the second, its start and points a little different.
        var archive = Path.Combine(folder, "export_nearest.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            void Add(string name, byte[] content)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(content);
            }
            Add("activities.csv", Encoding.UTF8.GetBytes("""
                Activity ID,Activity Date,Activity Name,Activity Type,Filename
                401,"Jul 1, 2024, 7:00:50 AM",Second Watch,Run,activities/401.gpx
                """.ReplaceLineEndings("\n")));
            Add("activities/401.gpx", TrackFiles.Gpx(SampleStravaArchive.AlongThePavement(Start.AddSeconds(50)).Select(f => f with { Lat = f.Lat + 0.000001 }), "running"));
        }
        await StravaImporter.ImportAsync(_dataSource, archive);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public async Task Then_the_Outing_starting_nearest_gains_its_Strava_id()
    {
        await using var query = _dataSource.CreateCommand("select source_file from app.outings where strava_id = 401");
        Assert.That(await query.ExecuteScalarAsync(), Is.EqualTo("Second_Watch.gpx"));
    }
}
