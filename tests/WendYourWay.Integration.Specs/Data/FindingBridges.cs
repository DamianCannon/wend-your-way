using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Bridges over water (PLAN T046), places a Loop along a river can cross back, as the build inserts
/// them. On Fixtures/bridges.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FindingBridges
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("bridges.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<bool> OverWater(long wayId)
    {
        await using var query = _dataSource.CreateCommand("select bool_or(bridge_over_water) from network.segments where osm_way_id = @way");
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        return (bool)(await query.ExecuteScalarAsync())!;
    }

    [TestCase(400, true, TestName = "Then_a_footbridge_over_a_river_is_a_bridge_over_water")]
    [TestCase(401, false, TestName = "Then_a_bridge_over_no_water_isn_t")]
    [TestCase(402, false, TestName = "Then_a_way_over_a_river_not_tagged_a_bridge_isn_t")]
    public async Task Then_a_bridge_over_water_is_known(long wayId, bool overWater) =>
        Assert.That(await OverWater(wayId), Is.EqualTo(overWater));

    [Test]
    public async Task Then_a_slice_carries_it()
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.335, 51.21), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new Core.Network.NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 3000);
        Assert.That(slice.Segments.Where(s => s.BridgeOverWater).Select(s => s.Key.OsmWayId).Distinct(), Is.EqualTo(new long[] { 400 }));
    }
}
