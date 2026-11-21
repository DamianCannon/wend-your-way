using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// Turn points in the exported route (PLAN T100, STORY-043): in the GPX as waypoints, their words as
/// names, Left or Right as symbols; and a TCX of the same route, which watches read for turn prompts,
/// its course points with the words as notes and its trackpoints timed at an even pace. Public
/// landmarks only.
/// </summary>
public class ExportingTurnPoints
{
    private static readonly XNamespace Gpx = "http://www.topografix.com/GPX/1/1";
    private static readonly XNamespace Tcx = "http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2";

    //  A 9.71 km Loop from Kenwood House round Parliament Hill and back, turning twice.
    private static readonly PlannedLoop Planned = new(
        Line: [new(51.5712, -0.1677), new(51.5655, -0.1641), new(51.5597, -0.1607), new(51.5650, -0.1500), new(51.5712, -0.1677)],
        Start: new(51.5712, -0.1677),
        Anchors: [],
        Breakdown: new Breakdown(LengthM: 9710, OffRoadPercent: 62, TrafficFreePercent: 17, ConnectorLengthM: 1990, RetraceLengthM: 340));

    private static readonly RouteTurn[] Turns =
    [
        new(new LatLon(51.5597, -0.1607), 2100, TurnKind.Left, "Left onto the footpath"),
        new(new LatLon(51.5650, -0.1500), 5200, TurnKind.SharpRight, "Sharp right onto Millfield Lane"),
    ];

    private static XElement[] Waypoints() => [.. XDocument.Parse(LoopGpx.Of(Planned, Turns).Content).Root!.Elements(Gpx + "wpt")];

    //  Each a little before its turn (T159: HearingATurnInTime).
    [Test]
    public void Each_turn_is_a_waypoint_in_the_GPX() =>
        Assert.That(Waypoints().Select(w => w.Element(Gpx + "name")!.Value), Is.EqualTo(new[] { "Left onto the footpath", "Sharp right onto Millfield Lane" }));

    [Test]
    public void Its_symbol_says_which_way() =>
        Assert.That(Waypoints().Select(w => w.Element(Gpx + "sym")!.Value), Is.EqualTo(new[] { "Left", "Right" }));

    [Test]
    public void The_GPX_with_its_turns_is_valid_GPX_1_1()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(Gpx.NamespaceName, Path.Combine(TestContext.CurrentContext.TestDirectory, "Schemas", "gpx-1.1.xsd"));
        var problems = new List<string>();
        XDocument.Parse(LoopGpx.Of(Planned, Turns).Content).Validate(schemas, (_, e) => problems.Add(e.Message));
        Assert.That(problems, Is.Empty);
    }

    [Test]
    public void With_no_turns_the_GPX_has_no_waypoints() =>
        Assert.That(XDocument.Parse(LoopGpx.Of(Planned).Content).Root!.Elements(Gpx + "wpt"), Is.Empty);

    private static XElement Course() => XDocument.Parse(LoopTcx.Of(Planned, Turns).Content).Descendants(Tcx + "Course").Single();

    [Test]
    public void The_TCX_is_a_course_of_the_route() =>
        Assert.That(Course().Descendants(Tcx + "Trackpoint").Count(), Is.EqualTo(Planned.Line.Count));

    [Test]
    public void Its_course_points_are_the_turns()
    {
        var points = Course().Elements(Tcx + "CoursePoint").ToArray();
        Assert.That(points.Select(p => (p.Element(Tcx + "PointType")!.Value, p.Element(Tcx + "Notes")!.Value)),
            Is.EqualTo(new[] { ("Left", "Left onto the footpath"), ("Right", "Sharp right onto Millfield Lane") }));
    }

    //  A course point's name is at most 10 characters, a course's 15 (Garmin's limits).
    [Test]
    public void Its_names_are_short_enough_for_a_watch() =>
        Assert.That((Course().Element(Tcx + "Name")!.Value.Length <= 15, Course().Elements(Tcx + "CoursePoint").All(p => p.Element(Tcx + "Name")!.Value.Length <= 10)), Is.EqualTo((true, true)));

    //  Timed as if run evenly: later along the route, later in time.
    [Test]
    public void Its_trackpoints_are_timed_in_order()
    {
        var times = Course().Descendants(Tcx + "Trackpoint").Select(t => DateTimeOffset.Parse(t.Element(Tcx + "Time")!.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.That(times.Zip(times.Skip(1), (a, b) => b > a), Is.All.True);
    }

    [Test]
    public void Its_course_points_are_timed_by_how_far_along_they_are()
    {
        var points = Course().Elements(Tcx + "CoursePoint").Select(p => DateTimeOffset.Parse(p.Element(Tcx + "Time")!.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var start = DateTimeOffset.Parse(Course().Descendants(Tcx + "Trackpoint").First().Element(Tcx + "Time")!.Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That((points[0] - start).TotalSeconds / (points[1] - start).TotalSeconds, Is.EqualTo(2100.0 / 5200).Within(0.01));
    }

    [Test]
    public void It_is_a_TCX_file_by_name() =>
        Assert.That(LoopTcx.Of(Planned, Turns).FileName, Is.EqualTo("wend-your-way-9.7km.tcx"));

    //  T112: named as the route is, its course name cut to Garmin's 15 characters.
    [Test]
    public void Then_its_file_and_course_are_named_from_the_route()
    {
        var tcx = LoopTcx.Of(Planned, Turns, name: "Kenwood House · 9.7 km");
        var course = System.Xml.Linq.XDocument.Parse(tcx.Content).Descendants(Tcx + "Course").Single();
        Assert.That((tcx.FileName, course.Element(Tcx + "Name")!.Value), Is.EqualTo(("kenwood-house-9.7km.tcx", "Kenwood House")));
    }

    //  A Map gap ahead (#150, T146b): not a turn, somewhere to look before going on.
    private static readonly RouteTurn[] WithAGap =
    [
        new(new LatLon(51.5597, -0.1607), 2100, TurnKind.MapGap, "A gap in the map (3 m): check it's passable"),
    ];

    [Test]
    public void A_Map_gap_is_a_waypoint_marked_as_danger_in_the_GPX()
    {
        var waypoint = XDocument.Parse(LoopGpx.Of(Planned, WithAGap).Content).Root!.Elements(Gpx + "wpt").Single();
        Assert.That((waypoint.Element(Gpx + "name")!.Value, waypoint.Element(Gpx + "sym")!.Value, waypoint.Element(Gpx + "type")!.Value),
            Is.EqualTo(("A gap in the map (3 m): check it's passable", "Danger Area", "Map gap")));
    }

    [Test]
    public void A_Map_gap_is_a_danger_course_point_in_the_TCX()
    {
        var point = XDocument.Parse(LoopTcx.Of(Planned, WithAGap).Content).Descendants(Tcx + "CoursePoint").Single();
        Assert.That((point.Element(Tcx + "Name")!.Value, point.Element(Tcx + "PointType")!.Value, point.Element(Tcx + "Notes")!.Value),
            Is.EqualTo(("Map gap", "Danger", "A gap in the map (3 m): check it's passable")));
    }
}
