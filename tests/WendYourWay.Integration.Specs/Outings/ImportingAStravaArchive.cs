using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Integration.Specs.Support;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// The user's whole history from Strava's bulk export (PLAN T029, increment 4 pulled forward): a
/// zip of activities.csv and the activities' files, read in place. On Fixtures/same-ground.osm,
/// with invented recordings along road 200's north pavement by Gospel Oak.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ImportingAStravaArchive
{
    private NpgsqlDataSource _dataSource = null!;
    private string _archive = null!;
    private StravaImport _first = null!;
    private StravaImport _again = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);

        //  Run 106, imported earlier from the GPX the user downloaded from Strava.
        var earlier = Path.Combine(Directory.CreateTempSubdirectory("cutthrough-earlier-").FullName, "Already_Here.gpx");
        await System.IO.File.WriteAllBytesAsync(earlier, TrackFiles.Gpx(SampleStravaArchive.AlongThePavement(SampleStravaArchive.AlreadyStart), "running", "Already Here"));
        await OutingImporter.ImportGpxAsync(_dataSource, earlier);

        _archive = SampleStravaArchive.Build();
        _first = await StravaImporter.ImportAsync(_dataSource, _archive);
        _again = await StravaImporter.ImportAsync(_dataSource, _archive);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<List<(long StravaId, string Kind, string? Activity, string Name)>> Outings()
    {
        await using var query = _dataSource.CreateCommand(
            "select strava_id, kind::text, activity::text, name from app.outings order by strava_id");
        await using var reader = await query.ExecuteReaderAsync();
        var outings = new List<(long, string, string?, string)>();
        while (await reader.ReadAsync())
            outings.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
        return outings;
    }

    //  The run, walk and road ride from GPX, TCX and FIT; the run already there gains its Strava
    //  id. The road ride is kept but informs no Activity (user, 2026-10-18).
    [Test]
    public async Task Then_runs_walks_and_rides_are_Outings_of_their_kind() =>
        Assert.That(await Outings(), Is.EqualTo(new (long, string, string?, string)[]
        {
            (101, "run", "run", "Pavement, then home"),
            (102, "walk", "run", "Sunday Walk"),
            (103, "ride", null, "Road Ride"),
            (106, "run", "run", "Already Here"),
            (107, "run", "run", "Second Watch"),
        }));

    [Test]
    public void Then_it_reports_what_it_imported_and_what_was_already_there() =>
        Assert.That((_first.Imported, _first.AlreadyThere), Is.EqualTo((4, 1)));

    //  A ride on a trainer and a swim have no way on the ground.
    [Test]
    public void Then_activities_with_no_way_on_the_ground_are_skipped_by_type() =>
        Assert.That(_first.SkippedByType, Is.EquivalentTo(new Dictionary<string, int> { ["Virtual Ride"] = 1, ["Swim"] = 1 }));

    [Test]
    public async Task Then_every_Outing_it_imported_has_its_coverage()
    {
        await using var query = _dataSource.CreateCommand("select count(*) from app.outings where coverage is null");
        Assert.That(await query.ExecuteScalarAsync(), Is.Zero);
    }

    [Test]
    public void Then_importing_it_again_changes_nothing() =>
        Assert.That((_again.Imported, _again.AlreadyThere), Is.EqualTo((0, 5)));
}
