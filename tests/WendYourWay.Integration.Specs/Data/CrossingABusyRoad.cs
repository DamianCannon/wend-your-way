using Npgsql;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Main Roads crossed at real crossings, as a Run's slice is read (#149, T145c): its junctions on a
/// Main Road split into corners, by which way each Segment leaves them, crossed at the crossing
/// points the build kept. A ride's slice isn't split: a bike turns across a Main Road in its
/// traffic. On Fixtures/crossing-a-busy-road.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class CrossingABusyRoad
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("crossing-a-busy-road.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<NetworkSlice> SliceAsync(Activity activity = Activity.Run)
    {
        await using var query = _dataSource.CreateCommand("select ST_X(ST_StartPoint(geom)), ST_Y(ST_StartPoint(geom)) from network.segments where osm_way_id = 700 limit 1");
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        return await new PostgisNetworkSlices(_dataSource).AroundAsync(new NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 2000, activity);
    }

    // The Run's Path between two OSM nodes, and its Breakdown.
    private static Breakdown Run(NetworkSlice slice, long from, long to)
    {
        int Node(long id) => slice.Segments.Where(s => !s.IsCrossingLink).Select(s => s.Key.FromNodeId == id ? s.From : s.Key.ToNodeId == id ? s.To : -1).First(n => n >= 0);
        var path = PathFinder.Find(slice, PathEnd.AtNode(Node(from)), PathEnd.AtNode(Node(to)), new RunCostModel(ScoringWeights.Default))
            ?? throw new AssertionException($"No Path from {from} to {to}.");
        return Breakdown.Of(slice, new Loop(Node(from), [], path.Traversals));
    }

    [Test]
    public async Task Then_a_Run_crosses_it_at_the_lights_not_straight_over()
    {
        var slice = await SliceAsync();
        var path = PathFinder.Find(slice,
            PathEnd.AtNode(slice.Segments.First(s => s.Key.FromNodeId == 311).From),
            PathEnd.AtNode(slice.Segments.First(s => s.Key.ToNodeId == 312).To),
            new RunCostModel(ScoringWeights.Default))!;
        Assert.That(path.Traversals.Select(t => slice.Segments[t.Segment]).Where(s => s.IsCrossingLink).Select(s => s.Crossing),
            Is.EqualTo(new CrossingKind?[] { CrossingKind.Signals }));
    }

    //  Its far end is south of the road, but it leaves the junction on the north side.
    [Test]
    public async Task Then_a_side_road_is_on_the_side_it_leaves_the_junction()
    {
        var slice = await SliceAsync();
        Assert.That(Run(slice, 314, 311).MainRoadCrossings, Is.Zero);
    }

    [Test]
    public async Task Then_a_ride_s_slice_isn_t_split() =>
        Assert.That((await SliceAsync(Activity.GravelRide)).Segments.Count(s => s.IsCrossingLink), Is.Zero);
}
