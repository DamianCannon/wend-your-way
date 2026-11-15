using WendYourWay.Cli;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Integration.Specs.Support;
using Npgsql;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend outings import-downloads` (PLAN T130, STORY-048; user: "skip files that have already been
/// imported as we don't want duplicates and yes being able to import loose files is useful as I do
/// use other tracking apps like Footpath"; chose "Downloads, recorded only"): a stand-in Downloads
/// folder holding the sample Strava export, a recording, the same recording again, a planned route
/// (its times and type taken out, as Wend Your Way's own exports have neither: T133), a file that
/// isn't a track, and a note. On Fixtures/same-ground.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ImportingRunsFromDownloads
{
    private string _downloads = null!;
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Network()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [SetUp]
    public async Task Downloads()
    {
        await using (var none = _dataSource.CreateCommand("delete from app.outings; delete from app.deleted_outings; delete from app.map_updates"))
            await none.ExecuteNonQueryAsync();
        _downloads = Directory.CreateTempSubdirectory("cutthrough-downloads-").FullName;
        var recording = await File.ReadAllTextAsync(Fixtures.Path("pavement-and-park.gpx"));
        await File.WriteAllTextAsync(Path.Combine(_downloads, "Morning Run.gpx"), recording);
        await File.WriteAllTextAsync(Path.Combine(_downloads, "Morning Run (1).gpx"), recording);
        await File.WriteAllTextAsync(Path.Combine(_downloads, "n2-9np-10.8km.gpx"), System.Text.RegularExpressions.Regex.Replace(recording, "<time>[^<]*</time>|<type>[^<]*</type>", ""));
        await File.WriteAllTextAsync(Path.Combine(_downloads, "broken.gpx"), "this isn't GPX");
        await File.WriteAllTextAsync(Path.Combine(_downloads, "notes.txt"), "a note");
        File.Copy(SampleStravaArchive.Build(), Path.Combine(_downloads, "export_1.zip"));
    }

    [TearDown]
    public void NoDownloads() => Directory.Delete(_downloads, recursive: true);

    private Task<(int ExitCode, string Output)> Import() => Ct("outings", "import-downloads", "--folder", _downloads, "--database", TestDatabase.ConnectionString);

    private static async Task<(int ExitCode, string Output)> Ct(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(args, output);
        return (exitCode, output.ToString());
    }

    private async Task<long> Outings()
    {
        await using var count = _dataSource.CreateCommand("select count(*) from app.outings");
        return (long)(await count.ExecuteScalarAsync())!;
    }

    private async Task<(string Kind, string Outcome, string Message, string Stage)> LastJob()
    {
        await using var query = _dataSource.CreateCommand("select kind, outcome, coalesce(message, ''), coalesce(stage, '') from app.map_updates order by id desc limit 1");
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    [Test]
    public async Task The_export_and_the_recording_are_imported()
    {
        var (exitCode, output) = await Import();
        Assert.That(exitCode, Is.Zero, output);
        Assert.That((await LastJob()).Message, Does.Match(@"^Imported \d+ new runs \(\d+ from Strava's export, 1 from files\)\."));
    }

    [Test]
    public async Task The_same_recording_twice_is_in_once()
    {
        await Import();
        await using var same = _dataSource.CreateCommand("select count(*) from app.outings where source_file like 'Morning Run%'");
        Assert.That(((long)(await same.ExecuteScalarAsync())!, System.Text.RegularExpressions.Regex.IsMatch((await LastJob()).Message, @"Skipped: 1 already in, ")), Is.EqualTo((1L, true)));
    }

    [Test]
    public async Task A_planned_route_isnt_imported_and_is_counted_so()
    {
        await Import();
        Assert.That((await LastJob()).Message, Does.Contain("1 a planned route, not a recording"));
    }

    [Test]
    public async Task A_file_that_isnt_a_track_is_counted_as_not_read()
    {
        await Import();
        Assert.That((await LastJob()).Message, Does.Contain("1 couldn't be read: broken.gpx"));
    }

    [Test]
    public async Task Imported_again_nothing_new_comes_in()
    {
        await Import();
        var before = await Outings();
        var (exitCode, output) = await Import();
        Assert.That((exitCode, await Outings(), (await LastJob()).Message.StartsWith("No new runs to import.", StringComparison.Ordinal)), Is.EqualTo((0, before, true)), output);
    }

    //  T134: nothing new, nothing changed, so no backup.
    [Test]
    public async Task Nothing_new_and_it_doesnt_back_up()
    {
        await Import();
        await Import();
        Assert.That((await LastJob()).Stage, Is.Not.EqualTo("backing up"));
    }

    [Test]
    public async Task Something_new_and_it_backs_up()
    {
        await Import();
        Assert.That((await LastJob()).Stage, Is.EqualTo("backing up"));
    }

    [Test]
    public async Task One_the_user_deleted_isnt_imported_again()
    {
        await Import();
        await using (var id = _dataSource.CreateCommand("select id from app.outings where source_file = 'Morning Run (1).gpx' or source_file = 'Morning Run.gpx' limit 1"))
            await new WendYourWay.Data.Outings.PostgisOutingViews(_dataSource).DeleteAsync((int)(await id.ExecuteScalarAsync())!);
        await Import();
        Assert.That((await LastJob()).Message, Does.Contain("deleted by you"));
    }

    [Test]
    public async Task It_is_a_job_of_its_own_that_finished()
    {
        await Import();
        var (kind, outcome, _, _) = await LastJob();
        Assert.That((kind, outcome), Is.EqualTo((JobKind.Import, "finished")));
    }

    [Test]
    public async Task Another_job_running_it_waits_its_turn()
    {
        await using var held = await UpdateLock.TryTakeAsync(TestDatabase.ConnectionString);
        var (exitCode, output) = await Import();
        Assert.That((exitCode, output.Contains("already running", StringComparison.Ordinal), await Outings()), Is.EqualTo((1, true, 0L)), output);
    }

    [Test]
    public async Task With_no_such_folder_it_says_so()
    {
        var (exitCode, output) = await Ct("outings", "import-downloads", "--folder", Path.Combine(_downloads, "missing"), "--database", TestDatabase.ConnectionString);
        Assert.That((exitCode, (await LastJob()).Outcome, output.Contains("no folder", StringComparison.Ordinal)), Is.EqualTo((1, "failed", true)), output);
    }
}
