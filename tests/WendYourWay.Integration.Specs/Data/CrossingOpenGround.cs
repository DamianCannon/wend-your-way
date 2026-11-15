using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Integration.Specs.Support;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Open ground (PLAN T039, STORY-038): straight Off-road crossings of public open land, between
/// junctions on the edge of a field its paths enclose, where going round is a real detour. On
/// Fixtures/open-ground.osm, where features are 1 km apart and nodes are numbered by feature.
/// </summary>
[TestFixture]
[NonParallelizable]
public class CrossingOpenGround
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("open-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    // Each crossing, by its ends (lower node id first): Open ground has no OSM way, so way id 0.
    private async Task<List<(long From, long To)>> Crossings()
    {
        await using var query = _dataSource.CreateCommand(
            "select least(from_node_id, to_node_id), greatest(from_node_id, to_node_id) from network.segments where osm_way_id = 0 order by 1, 2");
        await using var reader = await query.ExecuteReaderAsync();
        var crossings = new List<(long, long)>();
        while (await reader.ReadAsync()) crossings.Add((reader.GetInt64(0), reader.GetInt64(1)));
        return crossings;
    }

    //  T021: open land gives no motor vehicle a right, and every Segment has a Trail Ride's access,
    //  or a Trail Ride's slice would say the map needs rebuilding.
    [Test]
    public async Task Then_a_Trail_Ride_may_not_cross_open_ground()
    {
        await using var query = _dataSource.CreateCommand("""
            select count(*) filter (where osm_way_id = 0 and access_trail = 'forbidden' and closed_months = 0),
                   count(*) filter (where osm_way_id = 0),
                   count(*) filter (where access_trail is null or closed_months is null)
              from network.segments
            """);
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.That((reader.GetInt64(0) == reader.GetInt64(1) && reader.GetInt64(1) > 0, reader.GetInt64(2)), Is.EqualTo((true, 0L)));
    }

    //  1: a recreation ground 300 m square, the footways ending at 112 on its west edge and 113 on
    //  its east; going round its edge is 600 m.
    [Test]
    public async Task Then_a_field_is_crossed_between_the_paths_that_meet_its_edge() =>
        Assert.That(await Crossings(), Does.Contain((112L, 113L)));

    //  2: an L-shaped recreation ground, footways ending at 212 on one arm and 213 on the other.
    //  The straight line between them cuts the missing corner, and there's no junction to turn at.
    [Test]
    public async Task Then_a_crossing_never_leaves_the_field() =>
        Assert.That(await Crossings(), Has.None.Matches<(long From, long To)>(c => c.From is >= 200 and < 300));

    //  3: a park with a golf course across its middle, 4: a recreation ground tagged private, 5: a
    //  park with a pond in its middle; each with footways as in 1.
    [TestCase(300, TestName = "Then_a_golf_course_isn_t_crossed")]
    [TestCase(400, TestName = "Then_private_land_isn_t_crossed")]
    [TestCase(500, TestName = "Then_a_pond_isn_t_crossed")]
    public async Task Then_land_no_one_crosses_isn_t(long feature) =>
        Assert.That(await Crossings(), Has.None.Matches<(long From, long To)>(c => c.From >= feature && c.From < feature + 100));

    //  6: a recreation ground a footway already crosses from edge to edge: going round either half
    //  of it the shorter way is that footway.
    [Test]
    public async Task Then_a_field_a_path_already_crosses_isn_t_crossed_again() =>
        Assert.That(await Crossings(), Has.None.Matches<(long From, long To)>(c => c.From is >= 600 and < 700));

    //  7: a recreation ground with footways meeting the middle of each of its four edges. West to
    //  east and south to north would cross where there's no junction to turn at: one is kept.
    [Test]
    public async Task Then_a_fields_crossings_never_cross_each_other()
    {
        await using var query = _dataSource.CreateCommand("""
            select (select count(*) from network.segments where osm_way_id = 0 and from_node_id between 700 and 799),
                   (select count(*) from network.segments a join network.segments b on a.id < b.id and ST_Crosses(a.geom, b.geom)
                     where a.osm_way_id = 0 and b.osm_way_id = 0 and a.from_node_id between 700 and 799)
            """);
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.That((reader.GetInt64(0), reader.GetInt64(1)), Is.EqualTo((5L, 0L)));
    }

    //  8: a recreation ground 45 m square: going round saves only 45 m, less than the 50 m it must.
    [Test]
    public async Task Then_a_small_field_isn_t_crossed() =>
        Assert.That(await Crossings(), Has.None.Matches<(long From, long To)>(c => c.From is >= 800 and < 900));

    //  9: a recreation ground 300 m long and 80 m wide: going round saves 80 m, but it's only 1.27
    //  times as far, less than the 1.3 it must be.
    [Test]
    public async Task Then_a_long_narrow_field_isn_t_crossed_end_to_end() =>
        Assert.That(await Crossings(), Has.None.Matches<(long From, long To)>(c => c.From is >= 900 and < 1000));

    //  10: a recreation ground 300 m square, two footways meeting its west edge 10 m apart and one
    //  its east: crossing from either is the same crossing.
    [Test]
    public async Task Then_paths_side_by_side_share_one_crossing() =>
        Assert.That((await Crossings()).Count(c => c.From is >= 1000 and < 1100), Is.EqualTo(1));

    //  11: a heath 1.5 km square, as upland moor is mapped: no crossing over 1 km (2026-10-24: Great
    //  Britain's first build made them up to 10.9 km across moors).
    [Test]
    public async Task Then_a_moor_isn_t_crossed_further_than_a_km() =>
        Assert.That(await Crossings(), Has.None.Matches<(long From, long To)>(c => c.From is >= 1100 and < 1200));

    //  12: a common that is also military land. (The import keeps no area tagged only military:
    //  that waits for a re-import.)
    [Test]
    public async Task Then_military_land_isn_t_crossed() =>
        Assert.That(await Crossings(), Has.None.Matches<(long From, long To)>(c => c.From is >= 1200 and < 1300));

    //  13: a fence across the field, 14: a stream (T048).
    [TestCase(1300, TestName = "Then_a_field_isn_t_crossed_through_a_fence")]
    [TestCase(1400, TestName = "Then_a_field_isn_t_crossed_through_a_stream")]
    public async Task Then_a_field_isn_t_crossed_through_a_barrier(long feature) =>
        Assert.That(await Crossings(), Has.None.Matches<(long From, long To)>(c => c.From >= feature && c.From < feature + 100));

    //  Open ground is green Off-road ground, open to a Run.
    [Test]
    public async Task Then_a_crossing_is_open_green_Off_road()
    {
        await using var query = _dataSource.CreateCommand("""
            select ground::text, access_run::text, greenery, corridor is null, main_road
              from network.segments where osm_way_id = 0 and from_node_id = 112 and to_node_id = 113
            """);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That((reader.GetString(0), reader.GetString(1), reader.GetFloat(2), reader.GetBoolean(3), reader.GetBoolean(4)),
            Is.EqualTo(("off_road", "allowed", 1f, true, false)));
    }

    //  The planner's slice carries the crossing, joined to the footways at their ends, and a Path
    //  from one footway's far end to the other's goes across rather than round by the road.
    [Test]
    public async Task Then_a_Path_goes_across_the_field_rather_than_round_it()
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.2985, 51.2313), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 1500);
        int Junction(long nodeId)
        {
            var segment = slice.Segments.First(s => s.Key.FromNodeId == nodeId || s.Key.ToNodeId == nodeId);
            return segment.Key.FromNodeId == nodeId ? segment.From : segment.To;
        }

        var path = PathFinder.Find(slice, PathEnd.AtNode(Junction(111)), PathEnd.AtNode(Junction(114)), new RunCostModel(ScoringWeights.Default));
        Assert.That(path!.Traversals.Select(t => slice.Segments[t.Segment].Key), Does.Contain(new SegmentKey(0, 112, 113)));
    }

    //  An invented run along 1's west footway, straight across the field and on along the east
    //  one: a point every 25 m, 10 s apart. Without the crossing, its middle would be a gap.
    [Test]
    public async Task Then_an_Outing_across_the_field_covers_its_crossing()
    {
        var folder = Directory.CreateTempSubdirectory("cutthrough-open-ground-").FullName;
        try
        {
            var start = new System.DateTime(2025, 9, 6, 8, 0, 0, DateTimeKind.Utc);
            var fixes = Enumerable.Range(0, 29)
                .Select(i => new Fix(51.2313489, -0.3028722 + (i * 25 / 69_650.0), start.AddSeconds(10 * i))).ToArray();
            var gpx = Path.Combine(folder, "Across_the_field.gpx");
            await System.IO.File.WriteAllBytesAsync(gpx, TrackFiles.Gpx(fixes, "running", "Across the field"));
            await OutingImporter.ImportGpxAsync(_dataSource, gpx);

            await using var query = _dataSource.CreateCommand("""
                select exists (select 1 from app.outing_coverage where osm_way_id = 0 and from_node_id = 112 and to_node_id = 113),
                       (select count(*) from app.outing_gaps)
                """);
            await using var reader = await query.ExecuteReaderAsync();
            await reader.ReadAsync();
            Assert.That((reader.GetBoolean(0), reader.GetInt64(1)), Is.EqualTo((true, 0L)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
