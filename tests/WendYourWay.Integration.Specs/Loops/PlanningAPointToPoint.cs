using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// Point-to-points planned (T090): from Pembroke Lodge to a place about 2 km off as the crow flies,
/// on Fixtures/richmond-park.osm.gz; with no Distance a spread, with one about that long; nudged
/// as a Loop is.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PlanningAPointToPoint
{
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    private NpgsqlDataSource _dataSource = null!;
    private LoopPlanner _planner = null!;
    private LatLon _finish;
    private IOfferedLoops _spread = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("richmond-park.osm.gz"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _planner = new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default);
        // On the Network for certain: the point of a 10 km Loop from the Lodge furthest from it.
        var loop = (await _planner.PlanAsync(PembrokeLodge, distanceKm: 10))[0];
        _finish = loop.Line.MaxBy(p => Metres(p, PembrokeLodge));
        _spread = await _planner.PlanPointToPointAsync(PembrokeLodge, _finish);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private static double Metres(LatLon a, LatLon b)
    {
        var dy = (a.Latitude - b.Latitude) * 111_200;
        var dx = (a.Longitude - b.Longitude) * 111_200 * Math.Cos(a.Latitude * Math.PI / 180);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    //  The finish is a point on a Loop's line, perhaps partway along a Segment: a route ends at the Node nearest it. Within
    //  30 m till T199 (#203) changed that Loop, then 46 m off: half a long Segment in a park.
    private const double AtTheFinishM = 100;

    [Test]
    public void Each_goes_from_the_start_to_the_finish()
    {
        Assert.That(_spread.All(r => Metres(r.Line[0], PembrokeLodge) < 150 && Metres(r.Line[^1], _finish) < AtTheFinishM), Is.True);
    }

    [Test]
    public void With_no_Distance_a_spread_is_offered_shortest_first()
    {
        Assert.That((_spread.Count >= 2, _spread.Select(r => r.Breakdown.LengthM).Order().SequenceEqual(_spread.Select(r => r.Breakdown.LengthM))),
            Is.EqualTo((true, true)));
    }

    [Test]
    public async Task With_a_Distance_each_is_within_a_Loop_s_window()
    {
        var offered = await _planner.PlanPointToPointAsync(PembrokeLodge, _finish, distanceKm: 5);
        Assert.That((offered.Count > 0, offered.All(r => r.Breakdown.LengthM is >= 4000 and <= 6000)), Is.EqualTo((true, true)));
    }

    [Test]
    public void A_Distance_under_the_shortest_way_is_refused_saying_the_least()
    {
        Assert.That(() => _planner.PlanPointToPointAsync(PembrokeLodge, _finish, distanceKm: 1),
            Throws.InstanceOf<PointToPointRefusedException>().With.Message.StartsWith("At least ").And.Message.EndsWith(" km from the start to the end."));
    }

    //  Nudged, it still ends at the finish, and no length window is asked of it.
    [Test]
    public async Task Nudged_it_still_ends_at_the_finish()
    {
        var route = _spread[0];
        var nudged = await _spread.NudgeAsync(route, new AvoidNudge(route.Line[route.Line.Count / 2]), Leans.Centre);
        Assert.That((nudged is not null, Metres(nudged!.Loop.Line[^1], _finish) < AtTheFinishM, nudged.KmOutsideWindow), Is.EqualTo((true, true, 0.0)));
    }

    [Test]
    public void Too_far_apart_is_refused()
    {
        Assert.That(() => _planner.PlanPointToPointAsync(PembrokeLodge, new LatLon(PembrokeLodge.Latitude + 0.6, PembrokeLodge.Longitude)),
            Throws.InstanceOf<PointToPointRefusedException>().With.Message.EqualTo(LoopPlanner.TooFarApart));
    }
}
