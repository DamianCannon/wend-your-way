using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Corridors (PLAN T037): green ground on a walking route, or a national or international cycle
/// route, named as the build inserts it. On Fixtures/corridors.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class GivingSegmentsCorridors
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("corridors.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<string?> CorridorOf(long wayId)
    {
        await using var query = _dataSource.CreateCommand("select corridor from network.segments where osm_way_id = @way");
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        var corridor = await query.ExecuteScalarAsync();
        return corridor is DBNull ? null : (string?)corridor;
    }

    [Test]
    public async Task Then_a_footway_on_a_walking_route_is_on_its_Corridor() => Assert.That(await CorridorOf(100), Is.EqualTo("Downs Way"));

    //  Local and regional cycle routes are mostly fragments on quiet streets.
    [Test]
    public async Task Then_a_footway_on_a_local_cycle_route_isn_t() => Assert.That(await CorridorOf(101), Is.Null);

    [Test]
    public async Task Then_a_road_on_a_walking_route_isn_t() => Assert.That(await CorridorOf(102), Is.Null);

    [Test]
    public async Task Then_a_pavement_on_a_walking_route_isn_t() => Assert.That(await CorridorOf(106), Is.Null);

    [Test]
    public async Task Then_a_national_cycle_route_with_no_name_goes_by_its_ref() => Assert.That(await CorridorOf(103), Is.EqualTo("22"));

    [Test]
    public async Task Then_a_way_on_two_routes_is_on_the_longer() => Assert.That(await CorridorOf(104), Is.EqualTo("Downs Way"));

    //  The planner reads it with the slice.
    [Test]
    public async Task Then_a_slice_carries_it()
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.328, 51.26), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new Core.Network.NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 2000);
        Assert.That(slice.Segments.Where(s => s.Corridor is not null).Select(s => (s.Key.OsmWayId, s.Corridor)).Order(),
            Is.EqualTo(new[] { (100L, "Downs Way"), (103L, "22"), (104L, "Downs Way") }));
    }
}
