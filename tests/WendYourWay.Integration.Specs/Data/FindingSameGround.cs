using WendYourWay.Core.Network;
using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Paths that run beside a road are the road's ground for the metres they run beside it (R2.4,
/// CONTEXT.md: Connector, Retrace). Runs the real osm2pgsql and the real build on a hand-written
/// fixture (Fixtures/same-ground.osm).
/// </summary>
[TestFixture]
[NonParallelizable]
public class FindingSameGround
{
    //  Paths are sampled about every 10 m, so metres beside a road are good to about a sample.
    private const double SampleM = 10;

    private NpgsqlDataSource _dataSource = null!;
    private SameGroundBuild _report = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _report = (await NetworkBuild.Build(_dataSource)).SameGround;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<string> GroundOf(long wayId)
    {
        await using var query = _dataSource.CreateCommand("select ground::text from network.segments where osm_way_id = @way");
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        return (string)(await query.ExecuteScalarAsync())!;
    }

    /// <summary>The road Segments (way, from-node) a way's Segment runs beside, with the metres beside each.</summary>
    private async Task<Dictionary<(long Way, long From), double>> BesideOf(long wayId)
    {
        await using var query = _dataSource.CreateCommand("""
            select road.osm_way_id, road.from_node_id, b.metres
              from network.segments path
              join network.segment_beside b on b.path_id = path.id
              join network.segments road on road.id = b.road_id
             where path.osm_way_id = @way
            """);
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        await using var reader = await query.ExecuteReaderAsync();
        var beside = new Dictionary<(long, long), double>();
        while (await reader.ReadAsync()) beside[(reader.GetInt64(0), reader.GetInt64(1))] = reader.GetDouble(2);
        return beside;
    }

    //  The user's ruling: a pavement is a Connector, "because it's not where I would choose to
    //  run", whether or not OSM tags it as one.
    [Test]
    public async Task Then_a_pavement_beside_a_road_is_a_Connector() =>
        Assert.That(await GroundOf(210), Is.EqualTo("connector"));

    //  Out along one pavement and back along the other is a Retrace: both are the road's ground,
    //  all 104 m of them.
    [TestCase(210)]
    [TestCase(211)]
    public async Task Then_a_pavement_runs_beside_its_road_all_the_way(long wayId)
    {
        var beside = await BesideOf(wayId);
        Assert.That(beside.Keys, Is.EquivalentTo(new[] { (200L, 1L) }));
        Assert.That(beside[(200, 1)], Is.EqualTo(104).Within(1));
    }

    [Test]
    public async Task Then_a_cycle_track_beside_a_road_is_a_Connector_on_the_roads_ground()
    {
        Assert.That(await GroundOf(212), Is.EqualTo("connector"));
        Assert.That((await BesideOf(212)).Keys, Is.EquivalentTo(new[] { (201L, 2L) }));
    }

    //  The user is happy on dirt paths almost anywhere, and many in London run beside a road: only
    //  paved paths (pavements, cycle tracks) become Connectors. A dirt path keeps its ground, but
    //  is still the road's ground, so out along one and back along the other is a Retrace.
    [Test]
    public async Task Then_a_dirt_path_beside_a_road_stays_Off_road() =>
        Assert.That(await GroundOf(230), Is.EqualTo("off_road"));

    //  Only a path tagged unpaved: with no surface tag, a path beside a road is as likely a
    //  shared-use tarmac path as dirt, and mapped as a footway it would be a Connector (T038).
    [Test]
    public async Task Then_a_path_beside_a_road_with_no_surface_tag_is_a_Connector() =>
        Assert.That(await GroundOf(231), Is.EqualTo("connector"));

    //  Path 230 runs beside the road's west Segment (1-2) for 35 m and its east one (2-3) for
    //  125 m: it shares each for its own stretch, not all 160 m with one of them.
    [Test]
    public async Task Then_a_path_beside_two_Segments_of_a_road_shares_each_for_its_own_stretch()
    {
        var beside = await BesideOf(230);
        Assert.That(beside.Keys, Is.EquivalentTo(new[] { (200L, 1L), (200L, 2L) }));
        Assert.Multiple(() =>
        {
            Assert.That(beside[(200, 1)], Is.EqualTo(35).Within(SampleM));
            Assert.That(beside[(200, 2)], Is.EqualTo(125).Within(SampleM));
        });
    }

    [Test]
    public async Task Then_a_parallel_path_45_m_away_is_ground_of_its_own()
    {
        Assert.That(await GroundOf(220), Is.EqualTo("traffic_free"));
        Assert.That(await BesideOf(220), Is.Empty);
    }

    //  Only roads carry traffic. A path beside a pavement whose road isn't drawn, or beside an
    //  alley (Traffic-free, CONTEXT.md), is no Connector.
    [TestCase(220, Description = "beside pavement 221")]
    [TestCase(261, Description = "beside alley 260")]
    public async Task Then_a_path_beside_no_road_keeps_its_ground(long wayId) =>
        Assert.That(await GroundOf(wayId), Is.EqualTo("traffic_free"));

    //  Near a road all the way across, but at right angles to it.
    [Test]
    public async Task Then_a_path_crossing_a_road_is_not_beside_it()
    {
        Assert.That(await GroundOf(240), Is.EqualTo("traffic_free"));
        Assert.That(await BesideOf(240), Is.Empty);
    }

    //  Path 250 runs beside the road for 35 m, then 150 m across a park: it keeps its ground,
    //  but those 35 m are still the road's.
    [Test]
    public async Task Then_a_path_beside_a_road_for_only_a_short_way_keeps_its_ground_but_shares_that_stretch()
    {
        Assert.That(await GroundOf(250), Is.EqualTo("traffic_free"));
        var beside = await BesideOf(250);
        Assert.That(beside.Keys, Is.EquivalentTo(new[] { (200L, 2L) }));
        Assert.That(beside[(200, 2)], Is.EqualTo(35).Within(SampleM));
    }

    //  T199 (#203; user, of a cycle path beside a six-lane trunk road: "one which I avoid as much as possible even though
    //  it is technically green"): a big road (a dual carriageway, or 4+ lanes) reaches 25 m, not 12, as its carriageways
    //  are drawn down their own middles; a path 16 m from one is on its ground, one 30 m off isn't.
    [Test]
    public async Task Then_a_path_16_m_from_a_dual_carriageway_is_on_its_ground()
    {
        Assert.That(await GroundOf(310), Is.EqualTo("connector"));
        Assert.That((await BesideOf(310)).Keys, Is.EquivalentTo(new[] { (300L, 40L) }));
    }

    [Test]
    public async Task Then_a_path_30_m_from_a_dual_carriageway_keeps_its_ground() =>
        Assert.That((await GroundOf(311), await BesideOf(311)), Is.EqualTo(("traffic_free", new Dictionary<(long, long), double>())));

    //  An ordinary street still reaches 12 m.
    [Test]
    public async Task Then_a_path_16_m_from_a_street_keeps_its_ground() =>
        Assert.That((await GroundOf(340), await BesideOf(340)), Is.EqualTo(("traffic_free", new Dictionary<(long, long), double>())));

    //  T199 (user chose "Both, tagged speeds only"): a dirt path stays Off-road beside a road, but not beside one of 50 mph
    //  or more, nor beside a road tagged with no speed but the national limit; beside one with no speed tagged it stays.
    [Test]
    public async Task Then_a_dirt_path_beside_a_60_mph_road_is_a_Connector() =>
        Assert.That(await GroundOf(320), Is.EqualTo("connector"));

    [Test]
    public async Task Then_a_dirt_path_beside_a_road_with_no_speed_tagged_stays_Off_road() =>
        Assert.That(await GroundOf(330), Is.EqualTo("off_road"));

    [Test]
    public async Task Then_a_road_is_beside_nothing() =>
        Assert.That(await BesideOf(200), Is.Empty);

    [Test]
    public void Then_the_build_reports_paths_beside_roads() =>
        Assert.That(_report.PathsBesideRoads, Is.EqualTo(8));   // 210, 211, 212, 231, 310, 320, and 230 and 330 though they stay Off-road

    //  What the generator sees: each pavement on the road's ground, the far path on its own.
    [Test]
    public async Task Then_a_slice_of_the_Network_carries_the_same_ground()
    {
        await using var query = _dataSource.CreateCommand(
            "select ST_X(ST_StartPoint(geom)), ST_Y(ST_StartPoint(geom)) from network.segments where osm_way_id = 200 and from_node_id = 2");
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new NodePoint(reader.GetDouble(0), reader.GetDouble(1)), radiusM: 1000);

        IReadOnlyList<GroundShare> GroundOf(long way, long from) => slice.Segments.Single(s => s.Key.OsmWayId == way && s.Key.FromNodeId == from).SameGround;
        var road = GroundOf(200, 1).Single().Group;
        Assert.Multiple(() =>
        {
            Assert.That(new[] { GroundOf(210, 10), GroundOf(211, 12) }.Select(g => g.Single().Group), Is.All.EqualTo(road));
            Assert.That(GroundOf(220, 16).Single().Group, Is.Not.EqualTo(road));
        });
    }
}
