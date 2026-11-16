using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Crossings (PLAN T044): the Segments of crossing ways, with the kind of crossing, whether it's of
/// a Main Road, and each Segment's share of its crossing, as the build inserts them. On
/// Fixtures/crossings.osm, where each crossing way is cut in two by its road.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FindingCrossings
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("crossings.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    // The way's crossing kind and whether it's of a Main Road (from its Segments, which must agree),
    // and its Segments' shares added up.
    private async Task<(string? Kind, bool MainRoad, double Shares)> CrossingOf(long wayId)
    {
        await using var query = _dataSource.CreateCommand("""
            select min(crossing), bool_or(crossing_main_road), sum(crossing_share), count(distinct (crossing, crossing_main_road))
              from network.segments where osm_way_id = @way
            """);
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.That(reader.GetInt64(3), Is.EqualTo(1), $"way {wayId}'s Segments disagree");
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetBoolean(1), reader.GetDouble(2));
    }

    [TestCase(300, "signals", true, TestName = "Then_a_signalled_crossing_of_a_Main_Road_is_one")]
    [TestCase(301, "marked", true, TestName = "Then_a_zebra_crossing_of_a_Main_Road_is_marked")]
    [TestCase(302, "unmarked", true, TestName = "Then_an_unmarked_crossing_of_a_Main_Road_is_one")]
    //  Someone mapped a crossing there, so it's probably a proper one (user, 2026-10-24).
    [TestCase(303, "marked", false, TestName = "Then_a_crossing_with_no_kind_tagged_is_marked")]
    [TestCase(304, "signals", false, TestName = "Then_a_signalled_crossing_of_a_quiet_road_is_one")]
    public async Task Then_a_crossing_way_is_a_Crossing(long wayId, string kind, bool mainRoad) =>
        Assert.That(await CrossingOf(wayId), Is.EqualTo((kind, mainRoad, 1.0)).Using<(string?, bool, double)>(
            (a, b) => a.Item1 == b.Item1 && a.Item2 == b.Item2 && Math.Abs(a.Item3 - b.Item3) < 1e-6));

    [Test]
    public async Task Then_a_footway_that_isn_t_a_crossing_way_isn_t_one() =>
        Assert.That((await CrossingOf(305)).Kind, Is.Null);

    //  The planner reads them with the slice: a Main Road's are charged where they cross it (T145c), below.
    [Test]
    public async Task Then_a_slice_carries_them()
    {
        var slice = await SliceAsync();
        Assert.That(slice.Segments.Where(s => s.Crossing is not null && !s.IsCrossingLink).GroupBy(s => s.Key.OsmWayId)
                .Select(g => (g.Key, g.First().Crossing, g.First().CrossesMainRoad, Math.Round(g.Sum(s => s.CrossingShare), 6))).Order(),
            Is.EqualTo(new[]
            {
                (303L, (Core.Network.CrossingKind?)Core.Network.CrossingKind.Marked, false, 1.0),
                (304L, Core.Network.CrossingKind.Signals, false, 1.0),
            }));
    }

    //  A Run's slice splits a Main Road's junctions into corners (T145c): a crossing way over it is
    //  charged once, as the crossing at the junction where it meets the road, not as itself too.
    [TestCase(300, Core.Network.CrossingKind.Signals)]
    [TestCase(301, Core.Network.CrossingKind.Marked)]
    [TestCase(302, Core.Network.CrossingKind.Unmarked)]
    public async Task Then_a_Main_Road_s_is_charged_at_the_junction_it_crosses_it(long way, Core.Network.CrossingKind kind)
    {
        var slice = await SliceAsync();
        var pieces = slice.Segments.Where(s => s.Key.OsmWayId == way).ToList();
        var junction = pieces.SelectMany(s => new[] { s.Key.FromNodeId, s.Key.ToNodeId }).GroupBy(n => n).Single(g => g.Count() > 1).Key;
        Assert.That((
                pieces.Select(s => s.Crossing).Distinct(),
                slice.Segments.Where(s => s.IsCrossingLink && s.Key.FromNodeId == junction).Select(s => s.Crossing).Distinct()),
            Is.EqualTo((new Core.Network.CrossingKind?[] { null }, new Core.Network.CrossingKind?[] { kind })));
    }

    private async Task<Core.Network.NetworkSlice> SliceAsync()
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.336, 51.225), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        return await new PostgisNetworkSlices(_dataSource).AroundAsync(new Core.Network.NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 2000);
    }
}
