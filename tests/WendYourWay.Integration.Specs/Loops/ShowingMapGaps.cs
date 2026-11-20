using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A route over a Map gap (#150, T146b) says so: the gap's ends and length on the Loop, for the map and
/// the Loop card, and a turn point at it in the GPX. On Fixtures/gaps.osm: feature 8, a footway off the
/// road at node 803, round 500 m of field to node 813, 3 m short of the road 100 m east of 803.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ShowingMapGaps
{
    // Feature 8's origin, 7 km east of the fixture's: in metres east and north of 51.2600, -0.3000.
    private static LatLon At(double east, double north) => new(51.26 + (north / 111_250), -0.30 + ((7000 + east) / 69_675));

    private NpgsqlDataSource _dataSource = null!;
    private PlannedLoop _route = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("gaps.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        var planner = new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default, matching: new RouteMatching(_dataSource));
        // From the footway's end at 813, over the gap, and west along the road to its end at 801.
        IReadOnlyList<LatLon> route = [At(100, 3), .. Along(At(100, 0), At(-200, 0)), At(-200, 0)];
        _route = (await planner.ImportAsync(route))[0];
    }

    // A GPX's points every 10 m from one to the other.
    private static IEnumerable<LatLon> Along(LatLon from, LatLon to)
    {
        var steps = (int)Math.Ceiling(Math.Max(Math.Abs(to.Latitude - from.Latitude) * 111_250, Math.Abs(to.Longitude - from.Longitude) * 69_675) / 10);
        for (var i = 0; i < steps; i++)
            yield return new LatLon(from.Latitude + ((to.Latitude - from.Latitude) * i / steps), from.Longitude + ((to.Longitude - from.Longitude) * i / steps));
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public void Then_the_route_has_the_Map_gap_it_crosses() =>
        Assert.That(_route.MapGaps.Select(g => Math.Round(g.LengthM)), Is.EqualTo(new[] { 3.0 }));

    [Test]
    public void Then_the_gap_runs_from_the_paths_end_to_the_road() =>
        Assert.That(_route.MapGaps.Select(g => (Metres(g.From, At(100, 3)) < 0.5, Metres(g.To, At(100, 0)) < 0.5)), Is.EqualTo(new[] { (true, true) }));

    //  T157 (#161): its notes, for Explain?, put on its line: the 300 m of road west, halfway along it.
    [Test]
    public void Then_its_notes_say_why_it_goes_as_it_does() =>
        Assert.That(_route.Notes.Where(n => n.Note.Kind == NoteKind.Road).Select(n => (Math.Round(n.Note.LengthM / 10) * 10, Metres(n.At, At(-50, 0)) < 20)),
            Is.EqualTo(new[] { (300.0, true) }));

    [Test]
    public async Task Then_its_turn_points_say_to_check_the_gap_is_passable()
    {
        var turns = await new RouteTurns(_dataSource).OfAsync(
            [(new SegmentKey(811, 803, 813), true), (new SegmentKey(811, 813, -813), true), (new SegmentKey(801, 803, -813), false)]);
        Assert.That(turns.Where(t => t.Kind == TurnKind.MapGap).Select(t => (t.Words, Metres(t.At, At(100, 3)) < 0.5)),
            Is.EqualTo(new[] { ("A gap in the map (3 m): check it's passable", true) }));
    }

    private static double Metres(LatLon a, LatLon b)
    {
        var dy = (a.Latitude - b.Latitude) * 111_250;
        var dx = (a.Longitude - b.Longitude) * 69_675;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
