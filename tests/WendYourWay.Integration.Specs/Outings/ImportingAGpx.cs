using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// STORY-030: a GPX file of one of the user's runs, imported with the Segments it covered and
/// the gaps where it left the Network (PLAN T016). On Fixtures/same-ground.osm, with an invented
/// Run (Fixtures/pavement-and-park.gpx): east along road 200's north pavement, across side
/// street 201, then 140 m north over open ground.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ImportingAGpx
{
    private NpgsqlDataSource _dataSource = null!;
    private OutingImport _import = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _import = await OutingImporter.ImportGpxAsync(_dataSource, Fixtures.Path("pavement-and-park.gpx"));
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public async Task Then_it_is_kept_as_an_Outing()
    {
        await using var query = _dataSource.CreateCommand("select name, activity::text, started_at, source_file from app.outings");
        await using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(
            (reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3)),
            Is.EqualTo(("Pavement and Park", "run", new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero), "pavement-and-park.gpx")));
        Assert.That(await reader.ReadAsync(), Is.False, "more than one Outing");
    }

    [Test]
    public async Task Then_importing_it_again_changes_nothing()
    {
        var again = await OutingImporter.ImportGpxAsync(_dataSource, Fixtures.Path("pavement-and-park.gpx"));
        await using var count = _dataSource.CreateCommand("select count(*) from app.outings");
        Assert.That((again.AlreadyImported, (long)(await count.ExecuteScalarAsync())!), Is.EqualTo((true, 1L)));
    }

    private async Task<List<(long Way, long From)>> Covered()
    {
        await using var query = _dataSource.CreateCommand("select osm_way_id, from_node_id from app.outing_coverage order by 1, 2");
        await using var reader = await query.ExecuteReaderAsync();
        var covered = new List<(long, long)>();
        while (await reader.ReadAsync()) covered.Add((reader.GetInt64(0), reader.GetInt64(1)));
        return covered;
    }

    //  It ran the whole of road 200's west Segment along its north pavement: that pavement, the
    //  road and the south pavement are one stretch of ground, covered (AC1, AC4). It only crossed
    //  side street 201, and ran beside dirt path 230 for less than half of it: neither is covered.
    [Test]
    public async Task Then_the_ground_it_ran_along_is_covered_and_nothing_it_only_crossed() =>
        Assert.That(await Covered(), Is.EqualTo(new[] { (200L, 1L), (210L, 10L), (211L, 12L) }));

    //  North of road 200 the track crosses 140 m of open ground, more than 25 m from any way (AC3).
    [Test]
    public void Then_the_stretch_away_from_every_way_is_a_gap_with_its_length()
    {
        Assert.That(_import.GapsM, Has.Count.EqualTo(1));
        Assert.That(_import.GapsM[0], Is.EqualTo(142).Within(10));
    }

    //  159 of the track's 334 m are within 20 m of what it covered.
    [Test]
    public void Then_its_coverage_is_the_share_of_the_track_beside_what_it_covered() =>
        Assert.That(_import.Coverage, Is.EqualTo(0.48).Within(0.03));

    //  Segment identities can change when the Network is rebuilt, so coverage is worked out again:
    //  coverage of a Segment the Network no longer has (way 99999 here) goes.
    [Test]
    public async Task Then_a_Network_rebuild_works_out_its_coverage_again()
    {
        await using (var stale = _dataSource.CreateCommand(
            "insert into app.outing_coverage (outing_id, osm_way_id, from_node_id, to_node_id) select id, 99999, 1, 2 from app.outings"))
        {
            await stale.ExecuteNonQueryAsync();
        }
        await NetworkBuild.Build(_dataSource);
        Assert.That(await Covered(), Is.EqualTo(new[] { (200L, 1L), (210L, 10L), (211L, 12L) }));
    }
}
