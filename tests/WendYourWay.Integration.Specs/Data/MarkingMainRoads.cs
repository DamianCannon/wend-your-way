using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Main Roads (R2.1, CONTEXT.md) are marked by the build: trunk, primary and secondary roads and
/// their links, and the pavements beside them, which are the road's ground. A Loop then uses one
/// only for a short link (user, 2026-10-19). On Fixtures/main-road.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MarkingMainRoads
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("main-road.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<bool> IsMainRoad(long wayId)
    {
        await using var query = _dataSource.CreateCommand("select bool_and(main_road) from network.segments where osm_way_id = @way");
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        return (bool)(await query.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task Then_the_primary_road_is_a_Main_Road() => Assert.That(await IsMainRoad(100), Is.True);

    [Test]
    public async Task Then_the_pavement_beside_it_is_too() => Assert.That(await IsMainRoad(110), Is.True);

    [Test]
    public async Task Then_the_residential_street_isn_t() => Assert.That(await IsMainRoad(101), Is.False);

    [Test]
    public async Task Then_the_footpath_isn_t() => Assert.That(await IsMainRoad(102), Is.False);

    //  The planner reads it with the slice.
    [Test]
    public async Task Then_a_slice_carries_it()
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.302, 51.46), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new Core.Network.NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 1000);
        Assert.That(slice.Segments.Where(s => s.MainRoad).Select(s => s.Key.OsmWayId).Distinct().Order(), Is.EqualTo(new long[] { 100, 110 }));
    }
}
