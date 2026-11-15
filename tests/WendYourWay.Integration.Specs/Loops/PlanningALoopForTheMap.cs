using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// What the planner screen gets back for a Request: the Loop as a line on the map, its start and
/// Anchors, and its Breakdown. On the real London Network, from Kenwood House.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class PlanningALoopForTheMap
{
    private static readonly LatLon KenwoodHouse = new(51.5712, -0.1677);

    private NpgsqlDataSource _dataSource = null!;
    private IReadOnlyList<PlannedLoop> _offered = [];
    private PlannedLoop? _planned;
    private TerrainGrid? _terrain;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore();
        _terrain = RealNetwork.Terrain();
        var planner = new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default, _terrain);
        _offered = await planner.PlanAsync(KenwoodHouse, distanceKm: 10);
        _planned = _offered is [var best, ..] ? best : null;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    private static double MetresBetween(LatLon a, LatLon b)
    {
        var north = (a.Latitude - b.Latitude) * 111_200;
        var east = (a.Longitude - b.Longitude) * 111_200 * Math.Cos(a.Latitude * Math.PI / 180);
        return Math.Sqrt((north * north) + (east * east));
    }

    [Test]
    public void Then_the_line_starts_and_finishes_at_the_Loops_start()
    {
        Assert.That(_planned, Is.Not.Null);
        Assert.That(MetresBetween(_planned!.Line[0], _planned.Start), Is.LessThan(0.01));
        Assert.That(MetresBetween(_planned.Line[^1], _planned.Start), Is.LessThan(0.01));
    }

    [Test]
    public void Then_the_start_is_near_where_the_user_clicked() =>
        Assert.That(MetresBetween(_planned!.Start, KenwoodHouse), Is.LessThan(500));

    //  Up to three: two or three on a circle (T033c), one or two gates of a destination (T041),
    //  fewer once a spur to one is cut out (T042).
    [Test]
    public void Then_its_Anchors_are_given_for_numbering() =>
        Assert.That(_planned!.Anchors, Has.Count.InRange(0, 3));

    //  Five Loops to choose from (T052): from Kenwood House there's plenty of choice.
    [Test]
    public void Then_several_distinct_Loops_come_back_each_ready_for_the_map()
    {
        Assert.That(_offered, Has.Count.InRange(2, 5));
        Assert.That(_offered.Select(l => l.Line.Count).Distinct().Count() + _offered.Select(l => l.Breakdown.LengthM).Distinct().Count(), Is.GreaterThan(2));
    }

    [Test]
    public void Then_its_Breakdown_comes_with_it() =>
        Assert.That(_planned!.Breakdown.LengthM, Is.InRange(9000, 11500));

    //  The elevation profile (T051): from the start round to the start again, along the line.
    [Test]
    public void Then_its_elevation_profile_runs_the_Loops_length()
    {
        if (_terrain is null) Assert.Ignore("No OS Terrain 50 tiles in data/terrain50/asc.");
        var profile = _planned!.Profile;
        Assert.That((profile[0].DistanceM, profile[^1].DistanceM / _planned.Breakdown.LengthM), Is.EqualTo((0.0, 1.0)).Using<(double, double)>((a, b) => a.Item1 == b.Item1 && Math.Abs(a.Item2 - b.Item2) < 0.01));
    }

    //  Hampstead Heath and the streets round it lie between about 30 m and 140 m above sea level.
    [Test]
    public void Then_its_elevation_profile_gives_the_grounds_height()
    {
        if (_terrain is null) Assert.Ignore("No OS Terrain 50 tiles in data/terrain50/asc.");
        Assert.That(_planned!.Profile.Select(p => p.HeightM), Is.All.InRange(30, 140));
    }

    //  Each height is placed on the map, to mark where the pointer is on the profile.
    [Test]
    public void Then_its_elevation_profile_is_placed_along_the_line()
    {
        if (_terrain is null) Assert.Ignore("No OS Terrain 50 tiles in data/terrain50/asc.");
        Assert.That(MetresBetween(_planned!.Profile[^1].At, _planned.Start), Is.LessThan(1));
    }
}
