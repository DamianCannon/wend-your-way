using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// Include this time (#150, T146c): a route near a possible Map gap has it drawn, to include; included, the
/// route goes through it, and it's a Map gap the route crosses. On Fixtures/gaps.osm: feature 3, a road
/// east to west, and a footway ending 8 m short of it at its middle.
/// </summary>
[TestFixture]
[NonParallelizable]
public class IncludingAMapGap
{
    // Feature 3's origin, 2 km east of the fixture's: in metres east and north of 51.2600, -0.3000.
    private static LatLon At(double east, double north) => new(51.26 + (north / 111_250), -0.30 + ((2000 + east) / 69_675));

    private NpgsqlDataSource _dataSource = null!;
    private IOfferedLoops _offered = null!;
    private PlannedLoop _route = null!;
    private NudgedLoop? _included;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("gaps.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        var planner = new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default, matching: new RouteMatching(_dataSource));
        // West to east along the road, end to end, past the footway's end.
        _offered = await planner.ImportAsync([.. Enumerable.Range(0, 41).Select(i => At(-200 + (10 * i), 0))]);
        _route = _offered[0];
        _included = await _offered.NudgeAsync(_route, new IncludeNudge(At(0, 4)), Leans.Centre);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public void Then_the_possible_gap_near_it_is_drawn_from_its_end_on_the_route() =>
        Assert.That(_route.PossibleMapGaps.Select(g => (Math.Round(g.LengthM), Metres(g.From, At(0, 0)) < 0.5)), Is.EqualTo(new[] { (8.0, true) }));

    [Test]
    public void Then_it_crosses_no_Map_gap_till_one_is_included() => Assert.That(_route.MapGaps, Is.Empty);

    //  Through and back, as the footway beyond it is a dead end: one gap, crossed twice, told once.
    [Test]
    public void Included_the_route_goes_through_it() =>
        Assert.That(_included?.Loop.MapGaps.Select(g => Math.Round(g.LengthM)), Is.EqualTo(new[] { 8.0 }));

    [Test]
    public void Included_it_isn_t_drawn_as_possible_any_more() =>
        Assert.That(_included?.Loop.PossibleMapGaps, Is.Empty);

    //  A click nowhere near a possible gap includes nothing.
    [Test]
    public async Task A_click_away_from_any_possible_gap_does_nothing() =>
        Assert.That(await _offered.NudgeAsync(_route, new IncludeNudge(At(150, 60)), Leans.Centre), Is.Null);

    private static double Metres(LatLon a, LatLon b)
    {
        var dy = (a.Latitude - b.Latitude) * 111_250;
        var dx = (a.Longitude - b.Longitude) * 69_675;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
