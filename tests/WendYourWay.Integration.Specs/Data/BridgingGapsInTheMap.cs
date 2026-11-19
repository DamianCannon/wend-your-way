using WendYourWay.Core.Network;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Integration.Specs.Support;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Short gaps in the map (#150, T146a, STORY-051): a path that dead-ends near another way it isn't
/// already joined to is joined to it, under 5 m, or kept as a possible gap, 5 m to 15 m, unless a
/// fence, wall, hedge, waterway or railway is mapped across. Roads that dead-end are left alone. On
/// Fixtures/gaps.osm, where features are 1 km apart and nodes and ways are numbered by feature.
/// </summary>
[TestFixture]
[NonParallelizable]
public class BridgingGapsInTheMap
{
    private NpgsqlDataSource _dataSource = null!;
    private NetworkBuildReport _report = null!;
    private List<Gap> _gaps = null!;

    private sealed record Gap(long Way, long From, long To, string Kind, double LengthM, string AccessRun, string AccessRide, string? AccessTrail);

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("gaps.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _report = await NetworkBuild.Build(_dataSource);

        await using var query = _dataSource.CreateCommand("""
            select osm_way_id, from_node_id, to_node_id, gap, length_m, access_run::text, access_ride::text, access_trail::text
              from network.segments where gap is not null order by from_node_id
            """);
        await using var reader = await query.ExecuteReaderAsync();
        _gaps = [];
        while (await reader.ReadAsync())
            _gaps.Add(new Gap(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3), reader.GetDouble(4),
                reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private IEnumerable<Gap> In(int feature) => _gaps.Where(g => g.From / 100 == feature || g.From / 100 == -feature);

    // A way's Segments, by their ends.
    private async Task<List<(long From, long To)>> SegmentsOf(long way)
    {
        await using var query = _dataSource.CreateCommand(
            "select from_node_id, to_node_id from network.segments where osm_way_id = @way and gap is null order by from_node_id, to_node_id");
        query.Parameters.Add(new NpgsqlParameter("way", way));
        await using var reader = await query.ExecuteReaderAsync();
        var segments = new List<(long, long)>();
        while (await reader.ReadAsync()) segments.Add((reader.GetInt64(0), reader.GetInt64(1)));
        return segments;
    }

    //  1: a footway ending 3 m short of a road, with no point of the road near: joined to the
    //  nearest spot on it, a new point there.
    [Test]
    public void Then_a_path_ending_3_m_from_a_road_is_joined_to_it() =>
        Assert.That(In(1).Select(g => (g.Way, g.From, g.To, g.Kind, Math.Round(g.LengthM))), Is.EqualTo(new[] { (111L, 112L, -112L, "joined", 3.0) }));

    [Test]
    public async Task Then_the_road_is_cut_where_the_gap_meets_it() =>
        Assert.That(await SegmentsOf(101), Is.EquivalentTo(new[] { (101L, -112L), (-112L, 102L) }));

    //  2: a footway ending 1 m short of a road, 1.5 m from the road's own point: hardly further,
    //  so joined there, and the road cut at its point.
    [Test]
    public void Then_a_gap_meets_the_roads_own_point_when_that_is_hardly_further() =>
        Assert.That(In(2).Select(g => (g.From, g.To, g.Kind)), Is.EqualTo(new[] { (212L, 203L, "joined") }));

    [Test]
    public async Task Then_the_road_is_cut_at_its_own_point() =>
        Assert.That(await SegmentsOf(201), Is.EquivalentTo(new[] { (201L, 203L), (203L, 202L) }));

    //  3: a footway ending 8 m short: kept as a possible gap.
    [Test]
    public void Then_a_gap_of_5_to_15_m_is_kept_as_possible() =>
        Assert.That(In(3).Select(g => (g.From, g.Kind, Math.Round(g.LengthM))), Is.EqualTo(new[] { (312L, "possible", 8.0) }));

    //  4: 20 m short; 5: 3 m short, a fence between; 6: a road ending 3 m short; 7: a spur that left
    //  the road 15 m back; 11: a private footway; 13: a footway beside the road, ending 6 m from it;
    //  14: a footway round a field, ending 3 m from the road's own point it left from (it would be
    //  a second Segment of the footway between the same two points: the Great Britain build of
    //  2026-11-18 failed on one).
    [TestCase(4, TestName = "Then_a_path_ending_further_than_15_m_away_isn_t_joined")]
    [TestCase(5, TestName = "Then_a_gap_across_a_fence_isn_t_joined")]
    [TestCase(6, TestName = "Then_a_road_that_dead_ends_isn_t_joined")]
    [TestCase(7, TestName = "Then_a_spur_already_joined_to_the_road_nearby_isn_t_joined")]
    [TestCase(11, TestName = "Then_a_path_no_one_may_use_isn_t_joined")]
    [TestCase(13, TestName = "Then_a_path_beside_the_way_it_ends_near_isn_t_joined")]
    [TestCase(14, TestName = "Then_a_path_ending_near_the_point_it_left_from_isn_t_joined_there")]
    public void Then_it_isn_t_joined(int feature) => Assert.That(In(feature), Is.Empty);

    //  8: a footway that left the road and came back round 500 m of field, ending 3 m from it.
    [Test]
    public void Then_a_path_that_comes_back_round_to_its_road_is_joined() =>
        Assert.That(In(8).Select(g => (g.From, g.Kind)), Is.EqualTo(new[] { (813L, "joined") }));

    //  9: two footways end to end, 4 m apart: one gap between their ends, not one each way.
    [Test]
    public void Then_two_paths_ending_end_to_end_are_joined_once() =>
        Assert.That(In(9).Select(g => (g.From, g.To)), Is.EqualTo(new[] { (912L, 913L) }));

    //  10: a footway ending 3 m short of a bridleway: joined, and the gap is the footway's to use.
    [Test]
    public async Task Then_a_gap_is_open_to_whoever_may_use_its_path()
    {
        await using var query = _dataSource.CreateCommand(
            "select access_run::text, access_ride::text, access_trail::text from network.segments where osm_way_id = 1011 and gap is null");
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        var path = (reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
        Assert.That(In(10).Select(g => (g.AccessRun, g.AccessRide, g.AccessTrail)), Is.EqualTo(new[] { path }));
    }

    //  12: two footways end to end, 8 m apart: a possible gap, which joins no Off-road networks.
    [Test]
    public async Task Then_a_possible_gap_joins_no_off_road_networks()
    {
        await using var query = _dataSource.CreateCommand("""
            select count(distinct o.network_id) from network.segments s join network.segment_offroad o on o.segment_id = s.id
             where s.osm_way_id in (1211, 1213) and s.gap is null
            """);
        Assert.That((In(12).Select(g => g.Kind).ToList(), await query.ExecuteScalarAsync()), Is.EqualTo((new List<string> { "possible" }, 2L)));
    }

    //  A slice reads a joined gap as its path, to use; a possible gap as forbidden, used only when included.
    [TestCase(1, MapGapKind.Joined, AccessKind.Allowed, TestName = "Then_a_slice_reads_a_joined_gap_as_one_to_use")]
    [TestCase(3, MapGapKind.Possible, AccessKind.Forbidden, TestName = "Then_a_slice_reads_a_possible_gap_as_forbidden")]
    public async Task Then_a_slice_reads_a_gap(int feature, MapGapKind kind, AccessKind access)
    {
        var at = new NodePoint(0, 0);
        await using (var query = _dataSource.CreateCommand($"select ST_X(ST_StartPoint(geom)), ST_Y(ST_StartPoint(geom)) from network.segments where gap is not null and from_node_id = {feature}12"))
        await using (var reader = await query.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            at = new NodePoint(reader.GetDouble(0), reader.GetDouble(1));
        }
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(at, 50);
        Assert.That(slice.Segments.Where(s => s.MapGap != MapGapKind.None).Select(s => (s.MapGap, s.Access, s.Ground)), Is.EqualTo(new[] { (kind, access, GroundKind.TrafficFree) }));
    }

    [Test]
    public void Then_the_build_says_how_many_gaps_it_joined_and_kept() =>
        Assert.That((_report.Segments.GapsJoined, _report.Segments.GapsPossible), Is.EqualTo((5L, 2L)));
}
