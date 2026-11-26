using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// Wend It! (T194, #198; STORY-052): a route imported, found again through its turning points, its variations with less
/// road offered alone, each saying what it gains, and each nudgeable and savable as a Loop offered is. On
/// Fixtures/richmond-park.osm.gz, the route a Loop of Wend Your Way's own leaning to tarmac, as a GPX carries it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class WendingARoute
{
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    private NpgsqlDataSource _dataSource = null!;
    private IOfferedLoops _imported = null!;
    private IOfferedLoops _wended = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("richmond-park.osm.gz"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        var planner = new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default, matching: new RouteMatching(_dataSource));
        var tarmac = (await planner.PlanAsync(PembrokeLodge, distanceKm: 8, new Leans(0, -1)))[0];
        _imported = await planner.ImportAsync(tarmac.Line);
        _wended = (await _imported.WendItAsync(_imported[0]))!;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public void It_offers_variations_with_less_road() =>
        Assert.That(_wended.Select(v => v.Wended?.RoadSavedM), Is.Not.Empty.And.All.GreaterThan(0));

    [Test]
    public void The_most_road_saved_comes_first() =>
        Assert.That(_wended.Select(v => v.Wended!.RoadSavedM), Is.Ordered.Descending);

    [Test]
    public void Each_keeps_half_or_more_of_the_route() =>
        Assert.That(_wended.Select(v => v.Wended!.KeptShare), Is.All.InRange(WendIt.KeptAtLeast, 1));

    [Test]
    public void Each_can_be_saved_by_its_Segments() =>
        Assert.That(_wended.Select(v => _wended.TraversalsOf(v)), Is.All.Not.Null);

    [Test]
    public async Task Each_can_be_nudged() =>
        Assert.That(await _wended.NudgeAsync(_wended[0], new AvoidNudge(_wended[0].Line[_wended[0].Line.Count / 2]), Leans.Centre), Is.Not.Null);

    //  No two the same run again: each shares no more than 90% of its ground with another (found: five, each saving 83 m).
    [Test]
    public void No_two_are_alike()
    {
        var grounds = _wended.Select(v => _wended.TraversalsOf(v)!.Select(t => t.Key).ToHashSet()).ToArray();
        var alike = from a in Enumerable.Range(0, grounds.Length) from b in Enumerable.Range(0, grounds.Length)
                    where a < b && grounds[a].Intersect(grounds[b]).Count() > WendIt.MostAlike * Math.Min(grounds[a].Count, grounds[b].Count)
                    select (a, b);
        Assert.That(alike, Is.Empty);
    }

    //  A route Wend Your Way doesn't know: none.
    [Test]
    public async Task A_route_not_offered_here_has_none() =>
        Assert.That(await _wended.WendItAsync(_imported[0] with { }), Is.Null);
}
