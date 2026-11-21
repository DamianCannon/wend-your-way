using System.Globalization;
using System.Xml.Linq;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// A turn's waypoint put before its turn (T159, #163; user: "the waypoint was spoken after I'd
/// actually turned right and gone over it"): an app speaks a waypoint as it's reached, so each is
/// put back along the route by about ten seconds' going, and never more than half the way to the
/// turn before. Public landmarks only.
/// </summary>
public class HearingATurnInTime
{
    private static readonly XNamespace Gpx = "http://www.topografix.com/GPX/1/1";

    private static readonly LatLon Start = new(51.5712, -0.1677);

    private static LatLon Moved(LatLon from, double northM, double eastM) =>
        new(from.Latitude + (northM / 111_320), from.Longitude + (eastM / (111_320 * Math.Cos(from.Latitude * Math.PI / 180))));

    //  North 400 m, a left; 20 m on, a right; 300 m on, a right; and back to the start.
    private static readonly LatLon A = Moved(Start, 400, 0);
    private static readonly LatLon B = Moved(A, 0, 20);
    private static readonly LatLon C = Moved(B, 0, 300);

    private static readonly PlannedLoop Planned = new([Start, A, B, C, Start], Start, [], new Breakdown(1300, 50, 50, 0, 0));

    private static readonly RouteTurn[] Turns =
    [
        new(A, 400, TurnKind.Right, "Right onto the footpath"),
        new(B, 420, TurnKind.Left, "Left onto the bridge"),
        new(C, 720, TurnKind.Right, "Right onto the track"),
    ];

    private static LatLon[] Waypoints(PlannedLoop loop, RouteTurn[] turns, Activity activity = Activity.Run) =>
        [.. XDocument.Parse(LoopGpx.Of(loop, turns, activity: activity).Content).Root!.Elements(Gpx + "wpt")
            .Select(w => new LatLon(double.Parse(w.Attribute("lat")!.Value, CultureInfo.InvariantCulture), double.Parse(w.Attribute("lon")!.Value, CultureInfo.InvariantCulture)))];

    //  About ten seconds at a run: 30 m before the turn, on the way to it.
    [Test]
    public void A_Run_s_is_30_m_before_its_turn_on_the_route()
    {
        var heard = Waypoints(Planned, Turns)[0];
        Assert.That((heard.MetresTo(A), heard.MetresTo(Start)), Is.EqualTo((30.0, 370.0)).Using<(double, double)>((x, y) => Math.Abs(x.Item1 - y.Item1) < 0.5 && Math.Abs(x.Item2 - y.Item2) < 0.5));
    }

    [TestCase(Activity.GravelRide)]
    [TestCase(Activity.TrailRide)]
    public void A_ride_s_is_50_m_before(Activity ride) =>
        Assert.That(Waypoints(Planned, Turns, ride)[0].MetresTo(A), Is.EqualTo(50).Within(0.5));

    //  20 m after the turn before: put back 10 m, half the way, so the two are heard in order.
    [Test]
    public void One_close_after_another_is_put_back_half_the_way_to_it()
    {
        var heard = Waypoints(Planned, Turns)[1];
        Assert.That((heard.MetresTo(B), heard.MetresTo(A)), Is.EqualTo((10.0, 10.0)).Using<(double, double)>((x, y) => Math.Abs(x.Item1 - y.Item1) < 0.5 && Math.Abs(x.Item2 - y.Item2) < 0.5));
    }

    [Test]
    public void One_far_from_the_turn_before_is_put_back_the_whole_30_m() =>
        Assert.That(Waypoints(Planned, Turns)[2].MetresTo(C), Is.EqualTo(30).Within(0.5));

    //  A turn 40 m from the start: half the way back to it.
    [Test]
    public void The_first_close_to_the_start_is_put_back_half_the_way_to_it()
    {
        var near = Moved(Start, 40, 0);
        var loop = new PlannedLoop([Start, near, Moved(near, 0, 300), Start], Start, [], new Breakdown(700, 50, 50, 0, 0));
        Assert.That(Waypoints(loop, [new(near, 40, TurnKind.Left, "Left")])[0].MetresTo(near), Is.EqualTo(20).Within(0.5));
    }

    //  A Map gap is where it is: somewhere to look, not a turn to make.
    [Test]
    public void A_Map_gap_s_stays_where_the_gap_is() =>
        Assert.That(Waypoints(Planned, [new(A, 400, TurnKind.MapGap, "A gap in the map (3 m): check it's passable")])[0].MetresTo(A), Is.LessThan(0.1));

    //  A watch warns of a course point ahead itself: the TCX's stay at their turns.
    [Test]
    public void The_TCX_s_course_points_stay_at_their_turns()
    {
        XNamespace tcx = "http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2";
        var point = XDocument.Parse(LoopTcx.Of(Planned, Turns).Content).Descendants(tcx + "CoursePoint").First().Element(tcx + "Position")!;
        var at = new LatLon(double.Parse(point.Element(tcx + "LatitudeDegrees")!.Value, CultureInfo.InvariantCulture), double.Parse(point.Element(tcx + "LongitudeDegrees")!.Value, CultureInfo.InvariantCulture));
        Assert.That(at.MetresTo(A), Is.LessThan(0.1));
    }
}

/// <summary>The planner's GPX, downloaded or sent to the phone, its waypoints put back for the Activity chosen (T159).</summary>
public class ExportingForTheActivity
{
    private static readonly RouteTurn[] SomeTurns = [new(SampleLoops.ParliamentHill, 2100, TurnKind.Left, "Left onto the footpath")];

    private FakePhoneShare _phone = null!;
    private Web.Planning.PlannerSession _session = null!;

    [SetUp]
    public async Task Arrange()
    {
        _phone = new FakePhoneShare();
        _session = new Web.Planning.PlannerSession(new FakePlanner { Answer = SampleLoops.Planned }, new FakeSavedRoutes(), new FakeMarks(),
            new Web.Planning.PlannerSettings(SampleLoops.KenwoodHouse), new FakeLastRequest(), new FakeCarParks(), new FakePreviews(),
            new FakePlaceSearch(), new FakeRouteTurns { Turns = SomeTurns }, _phone, Microsoft.Extensions.Logging.Abstractions.NullLogger<Web.Planning.PlannerSession>.Instance);
        await _session.InitializeAsync();
        await _session.MapReadyAsync(new RecordingMap());
        await _session.ChooseActivityAsync(Activity.GravelRide);
    }

    [TearDown]
    public void Dispose() => _session.Dispose();

    private string Ride => LoopGpx.Of(SampleLoops.Planned, SomeTurns, _session.ExportedAs, Activity.GravelRide).Content;

    [Test]
    public async Task A_Gravel_Ride_s_GPX_has_a_ride_s_waypoints()
    {
        Assume.That(Ride, Is.Not.EqualTo(LoopGpx.Of(SampleLoops.Planned, SomeTurns, _session.ExportedAs).Content), "a ride's are put back further");
        Assert.That((await _session.GpxAsync())!.Content, Is.EqualTo(Ride));
    }

    [Test]
    public async Task Sent_to_the_phone_too()
    {
        await _session.PhoneAsync();
        Assert.That(_phone.Shared.Single().Content, Is.EqualTo(Ride));
    }
}
