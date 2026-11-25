using WendYourWay.Cli;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using Npgsql;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend update` (PLAN T127, STORY-047; user: "a single click way to update the application so that
/// it has the latest OSM maps ... and this needs to be robust so that failures in the middle don't
/// break the application"): the map brought up to date in one go, from a stand-in for Geofabrik
/// serving Fixtures/same-ground.osm. Each way it can fail leaves the map, and the extract, as they
/// were, and says so.
/// </summary>
[TestFixture]
[NonParallelizable]
public class UpdatingTheMapFromTheCommandLine
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 21, 22, 17, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);

    private ExtractServer _server = null!;
    private string _data = null!;

    [SetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        _server = new ExtractServer("same-ground", File.ReadAllBytes(Fixtures.Path("same-ground.osm")), Monday);
        _data = Directory.CreateTempSubdirectory("cutthrough-update-").FullName;
    }

    [TearDown]
    public void Dispose()
    {
        _server.Dispose();
        Directory.Delete(_data, recursive: true);
    }

    private static async Task<(int ExitCode, string Output)> Ct(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(args, output);
        return (exitCode, output.ToString());
    }

    private Task<(int ExitCode, string Output)> Update() => Ct("update", "--source", _server.ExtractUrl, "--os", _server.OsApi, "--data", _data,
        "--database", TestDatabase.ConnectionString, "--osm2pgsql", Tools.Osm2pgsql, "--rights-of-way", Fixtures.Path("rights-of-way"));

    private string Extract => Path.Combine(_data, "same-ground-latest.osm");

    private static async Task<T> Scalar<T>(string sql)
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static Task<long> Segments() => Scalar<long>("select count(*) from network.segments");

    private static Task<string> LastOutcome() => Scalar<string>("select outcome from app.map_updates order by id desc limit 1");

    private static Task<string> LastMessage() => Scalar<string>("select coalesce(message, '') from app.map_updates order by id desc limit 1");

    // A first update, done: the map built from Monday's extract.
    private async Task Updated()
    {
        var (exitCode, output) = await Update();
        Assert.That(exitCode, Is.Zero, output);
    }

    [Test]
    public async Task A_newer_map_is_downloaded_built_and_finished()
    {
        var (exitCode, output) = await Update();
        Assert.That((exitCode, await Segments() > 0, File.Exists(Extract), await LastOutcome()), Is.EqualTo((0, true, true, "finished")), output);
    }

    //  #152, T148: its steps counted as it goes, the last of them its backup (no terrain here, so no climbing).
    [Test]
    public async Task Finished_it_was_on_the_last_of_its_steps()
    {
        await Updated();
        Assert.That(await Scalar<string>("select coalesce(step, '') from app.map_updates order by id desc limit 1"), Is.EqualTo("step 19 of 19, backing up"));
    }

    //  T136: after it builds, Saved Routes the map changed under are moved onto it (none here).
    [Test]
    public async Task Built_it_moves_the_Saved_Routes_onto_the_new_map()
    {
        var (_, output) = await Update();
        Assert.That(output, Does.Contain("No Saved Route has had the map change under it."));
    }

    [Test]
    public async Task Finishing_one_stopped_after_its_swap_moves_them_too()
    {
        await Updated();
        await Scalar<object>("""
            update app.map_updates set stage = 'built', outcome = 'running', finished_at = null, heartbeat_at = now() - interval '1 hour'
             where id = (select max(id) from app.map_updates) returning id
            """);
        var (_, output) = await Update();
        Assert.That(output, Does.Contain("No Saved Route has had the map change under it."));
    }

    [Test]
    public async Task Up_to_date_nothing_is_downloaded()
    {
        await Updated();
        _server.Asked.Clear();
        var (exitCode, output) = await Update();
        Assert.That((exitCode, output.Contains("up to date", StringComparison.Ordinal), _server.Asked.Contains("/same-ground-latest.osm"), await LastOutcome()),
            Is.EqualTo((0, true, false, "up to date")), output);
    }

    //  T127: the old extract kept beside it, to build from again if a new map turns out bad.
    [Test]
    public async Task Updated_again_the_previous_extract_is_kept()
    {
        await Updated();
        var monday = await File.ReadAllBytesAsync(Extract);
        (_server.DataAt, _server.Extract) = (Tuesday, [.. monday, .. "\n"u8.ToArray()]);
        await Updated();
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(_data, "same-ground-prev.osm")), Is.EqualTo(monday));
    }

    //  Geofabrik's file and its .md5 can disagree for a while as it's made each day: the old map and
    //  extract kept, and a sentence saying to try again later.
    [Test]
    public async Task A_download_that_doesnt_match_its_md5_changes_nothing_and_says_so()
    {
        await Updated();
        var (monday, segments) = (await File.ReadAllBytesAsync(Extract), await Segments());
        (_server.DataAt, _server.Extract, _server.WrongMd5) = (Tuesday, [.. monday, .. "\n"u8.ToArray()], "0123456789abcdef0123456789abcdef");
        var (exitCode, output) = await Update();
        Assert.That((exitCode, await File.ReadAllBytesAsync(Extract), await Segments(), await LastOutcome()), Is.EqualTo((1, monday, segments, "failed")), output);
        Assert.That(await LastMessage(), Does.Contain("try again later"));
    }

    //  T129: without room for a build on the data's drive, it doesn't start: nothing downloaded.
    [Test]
    public async Task Without_room_for_it_nothing_is_done_and_it_says_so()
    {
        var (exitCode, output) = await Ct("update", "--source", _server.ExtractUrl, "--os", _server.OsApi, "--data", _data, "--database", TestDatabase.ConnectionString,
            "--osm2pgsql", Tools.Osm2pgsql, "--rights-of-way", Fixtures.Path("rights-of-way"), "--room", "1000000");
        Assert.That((exitCode, _server.Asked.Contains("/same-ground-latest.osm"), await LastOutcome(), await LastMessage()),
            Is.EqualTo((1, false, "failed", (await LastMessage()))), output);
        Assert.That(await LastMessage(), Does.StartWith("Not enough room to update the map"));
    }

    [Test]
    public async Task A_download_cut_off_changes_nothing()
    {
        await Updated();
        var (monday, segments) = (await File.ReadAllBytesAsync(Extract), await Segments());
        (_server.DataAt, _server.CutOffAfter) = (Tuesday, 100);
        var (exitCode, output) = await Update();
        Assert.That((exitCode, await File.ReadAllBytesAsync(Extract), await Segments(), await LastOutcome()), Is.EqualTo((1, monday, segments, "failed")), output);
    }

    //  An extract osm2pgsql can't read: the map as it was, the import marked unfinished, and a
    //  rebuild from it refused, as it would build from half an import.
    [Test]
    public async Task An_extract_that_cant_be_loaded_leaves_the_map_and_a_rebuild_is_refused()
    {
        await Updated();
        var segments = await Segments();
        (_server.DataAt, _server.Extract) = (Tuesday, "<osm version=\"0.6\"><node id=\"1\" lat=\"x\""u8.ToArray());
        var (exitCode, output) = await Update();
        var rebuild = await Ct("network", "rebuild", "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"));
        Assert.That((exitCode, await Segments(), await Scalar<string>("select state from osm.import"), rebuild.ExitCode),
            Is.EqualTo((1, segments, "importing", 1)), output + rebuild.Output);
        Assert.That(rebuild.Output, Does.Contain("didn't finish"));
    }

    //  An import that finished is marked so: a rebuild from it goes ahead.
    [Test]
    public async Task After_an_update_a_rebuild_from_its_extract_goes_ahead()
    {
        await Updated();
        var rebuild = await Ct("network", "rebuild", "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"));
        Assert.That(rebuild.ExitCode, Is.Zero, rebuild.Output);
    }

    [Test]
    public async Task A_second_update_while_one_runs_is_refused()
    {
        Migrations.Apply(TestDatabase.ConnectionString);
        await using var held = await UpdateLock.TryTakeAsync(TestDatabase.ConnectionString);
        var (exitCode, output) = await Update();
        Assert.That((held is not null, exitCode, output.Contains("already", StringComparison.Ordinal), _server.Asked.Count), Is.EqualTo((true, 1, true, 0)), output);
    }

    [Test]
    public async Task A_network_build_while_an_update_runs_is_refused()
    {
        await Updated();
        await using var held = await UpdateLock.TryTakeAsync(TestDatabase.ConnectionString);
        var (exitCode, output) = await Ct("network", "rebuild", "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"));
        Assert.That((exitCode, output.Contains("already", StringComparison.Ordinal)), Is.EqualTo((1, true)), output);
    }

    //  Stopped after its swap (the Outings not yet scored, the stars not calibrated): the next one
    //  finishes it, with no download.
    //  T131: "moving Saved Routes" (T136) comes after the swap too.
    [TestCase("built")]
    [TestCase("moving Saved Routes")]
    [TestCase("calibrating")]
    public async Task One_stopped_after_its_swap_is_finished_by_the_next_without_a_download(string stage)
    {
        await Updated();
        await Scalar<object>($"""
            update app.map_updates set stage = '{stage}', outcome = 'running', finished_at = null, heartbeat_at = now() - interval '1 hour'
             where id = (select max(id) from app.map_updates) returning id
            """);
        _server.Asked.Clear();
        var (exitCode, output) = await Update();
        Assert.That((exitCode, _server.Asked.Contains("/same-ground-latest.osm"), await LastOutcome(), output.Contains("Finished the update", StringComparison.Ordinal)),
            Is.EqualTo((0, false, "finished", true)), output);
    }
}
