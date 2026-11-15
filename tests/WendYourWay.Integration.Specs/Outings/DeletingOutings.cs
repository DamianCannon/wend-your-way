using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Integration.Specs.Support;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// Deleting an Outing (PLAN T106; user: "I'd like to be able to delete outings as there are loads of
/// them and so many are just duplicates or unwanted by me", "Gone, never re-imported"): its track,
/// coverage and gaps go, and no later Strava export or GPX import brings it back. On
/// Fixtures/same-ground.osm with the invented Strava archive and Runs.
/// </summary>
[TestFixture]
[NonParallelizable]
public class DeletingOutings
{
    private NpgsqlDataSource _dataSource = null!;
    private PostgisOutingViews _outings = null!;
    private string _folder = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _outings = new PostgisOutingViews(_dataSource);
        _folder = Directory.CreateTempSubdirectory("cutthrough-deleting-").FullName;
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        _dataSource.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [SetUp]
    public async Task NoOutings()
    {
        await using var command = _dataSource.CreateCommand("delete from app.outings; delete from app.deleted_outings;");
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> Count(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<int> IdOf(long stravaId) =>
        (int)(await Count($"select id::bigint from app.outings where strava_id = {stravaId}"));

    // A Run along the pavement, starting then, as its own GPX file.
    private async Task<string> Run(DateTime start, string name)
    {
        var path = Path.Combine(_folder, name + ".gpx");
        await File.WriteAllBytesAsync(path, TrackFiles.Gpx(SampleStravaArchive.AlongThePavement(start), "running", name));
        return path;
    }

    [Test]
    public async Task Deleted_it_is_no_longer_listed()
    {
        await StravaImporter.ImportAsync(_dataSource, SampleStravaArchive.Build());
        var before = await _outings.ListAsync();
        await _outings.DeleteAsync(await IdOf(101));
        Assert.That((await _outings.ListAsync()).Select(o => o.Name), Is.EqualTo(before.Select(o => o.Name).Where(n => n != "Pavement, then home")));
    }

    //  It no longer counts as ground covered: its coverage and gaps go with it.
    [Test]
    public async Task Its_coverage_and_gaps_go_with_it()
    {
        await StravaImporter.ImportAsync(_dataSource, SampleStravaArchive.Build());
        var id = await IdOf(101);
        Assert.That(await Count($"select count(*) from app.outing_coverage where outing_id = {id}"), Is.GreaterThan(0), "covered before");
        await _outings.DeleteAsync(id);
        Assert.That((await Count($"select count(*) from app.outing_coverage where outing_id = {id}"), await Count($"select count(*) from app.outing_gaps where outing_id = {id}")),
            Is.EqualTo((0L, 0L)));
    }

    [Test]
    public async Task The_same_Strava_export_imported_again_skips_it_and_says_so()
    {
        var archive = SampleStravaArchive.Build();
        await StravaImporter.ImportAsync(_dataSource, archive);
        await _outings.DeleteAsync(await IdOf(101));
        var again = await StravaImporter.ImportAsync(_dataSource, archive);
        Assert.That((again.Deleted, again.Imported, await Count("select count(*) from app.outings where strava_id = 101")), Is.EqualTo((1, 0, 0L)));
    }

    [Test]
    public async Task Its_own_GPX_imported_again_is_refused_saying_it_was_deleted()
    {
        var path = await Run(SampleStravaArchive.AlreadyStart, "Once");
        await OutingImporter.ImportGpxAsync(_dataSource, path);
        await _outings.DeleteAsync((await _outings.ListAsync()).Single().Id);
        Assert.That(() => OutingImporter.ImportGpxAsync(_dataSource, path), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("you deleted it"));
    }

    //  The same Outing from another device: its points differ, but it starts within a minute.
    [Test]
    public async Task The_same_Outing_recorded_on_another_device_is_refused_too()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, await Run(SampleStravaArchive.AlreadyStart, "Watch"));
        await _outings.DeleteAsync((await _outings.ListAsync()).Single().Id);
        Assert.That(() => OutingImporter.ImportGpxAsync(_dataSource, Run(SampleStravaArchive.AlreadyStart.AddSeconds(30), "Phone").GetAwaiter().GetResult()),
            Throws.InstanceOf<InvalidDataException>());
    }

    //  Another Outing over the same ground, on another day, is still imported.
    [Test]
    public async Task Another_Outing_over_the_same_ground_is_still_imported()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, await Run(SampleStravaArchive.AlreadyStart, "Monday"));
        await _outings.DeleteAsync((await _outings.ListAsync()).Single().Id);
        var another = await OutingImporter.ImportGpxAsync(_dataSource, await Run(SampleStravaArchive.AlreadyStart.AddDays(1), "Tuesday"));
        Assert.That((another.AlreadyImported, (await _outings.ListAsync()).Select(o => o.Name)), Is.EqualTo((false, new[] { "Tuesday" })));
    }

    [Test]
    public async Task Deleting_one_not_here_changes_nothing()
    {
        await _outings.DeleteAsync(99_999);
        Assert.That(await Count("select count(*) from app.deleted_outings"), Is.Zero);
    }
}
