using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Turning OSM ways into Segments: split at junctions, and only at junctions. Runs the real
/// osm2pgsql and the real build on a hand-written fixture (Fixtures/topology.osm).
/// </summary>
[TestFixture]
[NonParallelizable]
public class BuildingSegments
{
    private NpgsqlDataSource _dataSource = null!;
    private SegmentBuild _report = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("topology.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _report = (await NetworkBuild.Build(_dataSource)).Segments;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<List<(long From, long To)>> SegmentsOf(long wayId)
    {
        await using var query = _dataSource.CreateCommand(
            "select from_node_id, to_node_id from network.segments where osm_way_id = @way order by from_node_id, to_node_id");
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        await using var reader = await query.ExecuteReaderAsync();
        var ends = new List<(long, long)>();
        while (await reader.ReadAsync()) ends.Add((reader.GetInt64(0), reader.GetInt64(1)));
        return ends;
    }

    [Test]
    public async Task Then_ways_meeting_at_a_shared_node_are_split_there() =>
        Assert.That(await SegmentsOf(10), Is.EqualTo(new[] { (1L, 2L), (2L, 3L) }));

    [Test]
    public async Task Then_a_bridge_crossing_without_a_shared_node_splits_nothing() =>
        Assert.That(await SegmentsOf(30), Is.EqualTo(new[] { (6L, 7L) }));

    //  A Segment from a node back to itself can't be travelled, so a closed way (a path round
    //  a pond) is cut in two at its middle vertex.
    [Test]
    public async Task Then_a_closed_way_becomes_two_travellable_Segments() =>
        Assert.That(await SegmentsOf(40), Is.EqualTo(new[] { (8L, 9L), (9L, 8L) }));

    //  …but only when it has at most one junction. A loop already joined at two points is cut
    //  there and nowhere else: a Segment ends only where there's a choice.
    [Test]
    public async Task Then_a_closed_way_joined_at_two_points_is_cut_only_there() =>
        Assert.That(await SegmentsOf(90), Is.EqualTo(new[] { (30L, 31L), (31L, 30L) }));

    //  A way that comes back to a node partway along (a turning loop at the end of a path) would
    //  leave a piece from that node to itself; it's cut at its middle like a closed way.
    [Test]
    public async Task Then_a_way_that_loops_back_on_itself_becomes_travellable_Segments() =>
        Assert.That(await SegmentsOf(100), Is.EqualTo(new[] { (40L, 41L), (41L, 42L), (42L, 41L) }));

    [Test]
    public async Task Then_no_Segment_starts_and_ends_at_the_same_node()
    {
        await using var query = _dataSource.CreateCommand("select count(*) from network.segments where from_node_id = to_node_id");
        Assert.That(await query.ExecuteScalarAsync(), Is.Zero);
    }

    [Test]
    public async Task Then_a_node_shared_only_with_a_building_is_not_a_junction() =>
        Assert.That(await SegmentsOf(60), Is.EqualTo(new[] { (11L, 13L) }));

    [Test]
    public async Task Then_a_forbidden_way_still_makes_a_junction() =>
        Assert.That(await SegmentsOf(62), Is.EqualTo(new[] { (19L, 20L), (20L, 21L) }));

    [Test]
    public async Task Then_the_private_drive_itself_is_forbidden_for_a_Run()
    {
        await using var query = _dataSource.CreateCommand("select access_run::text from network.segments where osm_way_id = 63");
        Assert.That(await query.ExecuteScalarAsync(), Is.EqualTo("forbidden"));
    }

    [Test]
    public async Task Then_a_way_whose_nodes_and_geometry_disagree_is_skipped() =>
        Assert.That(await SegmentsOf(80), Is.Empty);

    [Test]
    public void Then_the_build_reports_ways_read() =>
        Assert.That(_report.WaysRead, Is.EqualTo(13));         // all but the road being built and the service yard

    [Test]
    public void Then_the_build_reports_ways_it_had_to_skip() =>
        Assert.That(_report.WaysMisaligned, Is.EqualTo(1));    // track 80

    [Test]
    public void Then_the_build_reports_Segments_written() =>
        Assert.That(_report.SegmentsWritten, Is.EqualTo(19));

    [Test]
    public void Then_the_build_reports_no_identity_clashes() =>
        Assert.That(_report.IdentityClashes, Is.Zero);

    [Test]
    public async Task Then_Segments_are_in_British_National_Grid_with_real_lengths()
    {
        //  1 → 2 is 0.001° of longitude at 51.56°N: about 69.2 m.
        await using var query = _dataSource.CreateCommand(
            "select ST_SRID(geom), length_m from network.segments where osm_way_id = 10 and from_node_id = 1");
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt32(0), Is.EqualTo(27700));
            Assert.That(reader.GetDouble(1), Is.EqualTo(69.2).Within(1.0));
        });
    }
}
