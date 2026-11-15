using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A route planned elsewhere, as a GPX gives it, matched onto the Network as a Loop so the scorer
/// can judge it (increment 1). Checked by a round trip on Fixtures/richmond-park.osm.gz: a Loop of
/// Wend Your Way's own, taken as a GPX would carry it, matches back to the same Loop.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MatchingARouteOntoTheNetwork
{
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    private NpgsqlDataSource _dataSource = null!;
    private PlannedLoop _planned = null!;
    private MatchedRoute _matched = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("richmond-park.osm.gz"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _planned = (await new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default)
            .PlanAsync(PembrokeLodge, distanceKm: 10))[0];
        _matched = await new RouteMatching(_dataSource).MatchAsync(_planned.Line);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public void Then_the_whole_route_is_joined_up() => Assert.That(_matched.Match.Unjoined, Is.Empty);

    [Test]
    public void Then_the_match_is_as_long_as_the_route() =>
        Assert.That(_matched.Match.LengthM, Is.EqualTo(_matched.RouteLengthM).Within(1).Percent);

    [Test]
    public void Then_the_route_runs_along_what_was_matched() =>
        Assert.That(_matched.ShareNearMatch, Is.GreaterThan(0.99));

    //  The same ground, not just the same length: matching a road as the path beside it would
    //  change these.
    [Test]
    public void Then_it_breaks_down_as_the_Loop_it_was()
    {
        var matched = Breakdown.Of(_matched.Slice, _matched.Match.Loop);
        Assert.Multiple(() =>
        {
            Assert.That(matched.OffRoadPercent, Is.EqualTo(_planned.Breakdown.OffRoadPercent).Within(1));
            Assert.That(matched.TrafficFreePercent, Is.EqualTo(_planned.Breakdown.TrafficFreePercent).Within(1));
        });
    }
}
