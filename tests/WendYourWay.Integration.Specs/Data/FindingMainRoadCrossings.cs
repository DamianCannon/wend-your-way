using Npgsql;
using WendYourWay.Data;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Main Road crossings (#149, T145a): where a Main Road may be crossed, as the build finds them. A
/// crossing point mapped on the road itself cuts it there, so a way can join it, and its kind is
/// kept; and each Segment says whether its road is busy (a trunk road, four lanes or more, or one
/// carriageway of a dual carriageway), where crossing with no crossing point is never chosen. On
/// Fixtures/main-road-crossings.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FindingMainRoadCrossings
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("main-road-crossings.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<List<T>> Read<T>(string sql, Func<NpgsqlDataReader, T> row)
    {
        await using var query = _dataSource.CreateCommand(sql);
        await using var reader = await query.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync()) rows.Add(row(reader));
        return rows;
    }

    [Test]
    public async Task Then_a_Main_Road_is_cut_at_each_crossing_point_on_it() =>
        Assert.That(await Read("select from_node_id from network.segments where osm_way_id = 500 order by from_node_id", r => r.GetInt64(0)),
            Is.EqualTo(new long[] { 101, 102, 103, 104 }));

    [Test]
    public async Task Then_each_crossing_point_on_a_Main_Road_keeps_its_kind() =>
        Assert.That(await Read("select node_id, kind from network.crossing_nodes order by node_id", r => (r.GetInt64(0), r.GetString(1))),
            Is.EqualTo(new[] { (102L, "marked"), (103L, "signals"), (104L, "unmarked"), (123L, "signals") }));

    //  Lights for vehicles only, or a point tagged crossing=no, aren't a place to cross; a quiet road's crossing points don't matter here.
    [Test]
    public async Task Then_no_crossing_and_a_quiet_road_s_crossing_points_don_t_cut_it() =>
        Assert.That(await Read("select osm_way_id, count(*) from network.segments where osm_way_id in (501, 505, 510) group by 1 order by 1", r => (r.GetInt64(0), r.GetInt64(1))),
            Is.EqualTo(new[] { (501L, 2L), (505L, 1L), (510L, 1L) }));

    //  A pavement beside a Main Road is one too, for its traffic, but it's the road whose sides are crossed.
    [Test]
    public async Task Then_a_Main_Road_s_carriageway_is_one_and_a_pavement_beside_it_isn_t() =>
        Assert.That(await Read("select osm_way_id, bool_and(main_road), bool_and(carriageway) from network.segments where osm_way_id in (500, 520) group by 1 order by 1",
                r => (r.GetInt64(0), r.GetBoolean(1), r.GetBoolean(2))),
            Is.EqualTo(new[] { (500L, true, true), (520L, true, false) }));

    [TestCase(500, false, TestName = "Then_a_primary_road_isn_t_busy")]
    [TestCase(502, true, TestName = "Then_a_trunk_road_is_busy")]
    [TestCase(503, true, TestName = "Then_a_road_of_four_lanes_is_busy")]
    [TestCase(504, true, TestName = "Then_one_carriageway_of_a_dual_carriageway_is_busy")]
    [TestCase(505, false, TestName = "Then_a_two_lane_secondary_road_isn_t_busy")]
    [TestCase(510, false, TestName = "Then_a_quiet_road_isn_t_busy")]
    public async Task Then_a_Segment_says_whether_its_road_is_busy(long way, bool busy) =>
        Assert.That(await Read($"select distinct busy from network.segments where osm_way_id = {way}", r => r.GetBoolean(0)), Is.EqualTo(new[] { busy }));
}
