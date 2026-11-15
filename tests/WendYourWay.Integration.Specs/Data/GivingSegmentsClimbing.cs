using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Climbing (PLAN T049): the metres each Segment climbs and descends from its from node to its to
/// node, sampled from OS Terrain 50 by the build. On Fixtures/climbing.osm and its terrain tile.
/// </summary>
[TestFixture]
[NonParallelizable]
public class GivingSegmentsClimbing
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("climbing.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource, new TerrainGrid(Fixtures.Path("terrain")));
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    // The way's climbing as it was drawn (its one Segment, from its first node to its last).
    private async Task<(double Ascent, double Descent)> ClimbingOf(long wayId)
    {
        await using var query = _dataSource.CreateCommand("select ascent_m, descent_m from network.segments where osm_way_id = @way");
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        await using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        return (reader.GetFloat(0), reader.GetFloat(1));
    }

    [Test]
    public async Task Then_a_path_up_a_slope_climbs_it()
    {
        var (ascent, descent) = await ClimbingOf(100);
        Assert.That((ascent, descent), Is.EqualTo((30.0, 0.0)).Using<(double, double)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 0.5 && Math.Abs(a.Item2 - b.Item2) < 0.5));
    }

    [Test]
    public async Task Then_a_path_across_a_valley_goes_down_into_it_and_up_out()
    {
        var (ascent, descent) = await ClimbingOf(102);
        Assert.That((ascent, descent), Is.EqualTo((20.0, 20.0)).Using<(double, double)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 0.5 && Math.Abs(a.Item2 - b.Item2) < 0.5));
    }

    //  The grid is the ground, not the deck.
    [TestCase(103, TestName = "Then_a_bridge_over_a_valley_is_level")]
    [TestCase(104, TestName = "Then_a_tunnel_under_a_valley_is_level")]
    public async Task Then_a_bridge_or_tunnel_is_level(long wayId) =>
        Assert.That(await ClimbingOf(wayId), Is.EqualTo((0.0, 0.0)).Using<(double, double)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 0.5 && Math.Abs(a.Item2 - b.Item2) < 0.5));

    //  The planner reads it with the slice.
    [Test]
    public async Task Then_a_slice_carries_it()
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.318, 51.25), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new Core.Network.NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 2000);
        var path = slice.Segments.Single(s => s.Key.OsmWayId == 100);
        Assert.That((Math.Round(path.AscentM), Math.Round(path.DescentM)), Is.EqualTo((30.0, 0.0)));
    }
}
