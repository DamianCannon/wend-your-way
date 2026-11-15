using System.Text;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A GPX to import as a route (T083): a planner's or a friend's. Its track's points, or a route's if
/// it has no track, and its name. Positions are invented, on Hampstead Heath.
/// </summary>
public class ReadingARouteGpx
{
    private static RouteGpx Read(string xml) => RouteGpx.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static readonly LatLon[] Heath = [new(51.5597, -0.1607), new(51.5605, -0.1615), new(51.5612, -0.1624)];

    [Test]
    public void A_track_is_read_with_its_name()
    {
        var gpx = Read("""
            <gpx xmlns="http://www.topografix.com/GPX/1/1" version="1.1">
              <metadata><name>From the metadata</name></metadata>
              <trk><name> Heath and back </name><trkseg>
                <trkpt lat="51.5597" lon="-0.1607"/><trkpt lat="51.5605" lon="-0.1615"/><trkpt lat="51.5612" lon="-0.1624"/>
              </trkseg></trk>
            </gpx>
            """);
        Assert.That((gpx.Name, gpx.Points), Is.EqualTo(("Heath and back", (IReadOnlyList<LatLon>)Heath)).Using<(string?, IReadOnlyList<LatLon>)>(
            (a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
    }

    //  A planner's route, GPX 1.0, with no track: its route points.
    [Test]
    public void A_route_with_no_track_is_read_from_its_route_points()
    {
        var gpx = Read("""
            <gpx xmlns="http://www.topografix.com/GPX/1/0" version="1.0">
              <rte><name>Planned</name>
                <rtept lat="51.5597" lon="-0.1607"/><rtept lat="51.5605" lon="-0.1615"/><rtept lat="51.5612" lon="-0.1624"/>
              </rte>
            </gpx>
            """);
        Assert.That((gpx.Name, gpx.Points.SequenceEqual(Heath)), Is.EqualTo(("Planned", true)));
    }

    [Test]
    public void One_with_no_name_has_none()
    {
        var gpx = Read("""<gpx xmlns="http://www.topografix.com/GPX/1/1"><trk><trkseg><trkpt lat="51.5597" lon="-0.1607"/></trkseg></trk></gpx>""");
        Assert.That(gpx.Name, Is.Null);
    }

    [Test]
    public void A_file_that_isn_t_a_GPX_is_refused() =>
        Assert.That(() => Read("<kml/>"), Throws.InstanceOf<InvalidDataException>());

    [Test]
    public void A_GPX_with_no_points_is_refused() =>
        Assert.That(() => Read("""<gpx xmlns="http://www.topografix.com/GPX/1/1"><trk><trkseg/></trk></gpx>"""),
            Throws.InstanceOf<InvalidDataException>().With.Message.EqualTo("That GPX has no route in it."));

    //  A GPX has no DTD: one with an entity in it is refused, not expanded.
    [Test]
    public void One_with_a_DTD_is_refused() =>
        Assert.That(() => Read("""
            <?xml version="1.0"?><!DOCTYPE gpx [<!ENTITY e "expanded">]>
            <gpx xmlns="http://www.topografix.com/GPX/1/1"><trk><name>&e;</name><trkseg><trkpt lat="51.5597" lon="-0.1607"/></trkseg></trk></gpx>
            """), Throws.InstanceOf<InvalidDataException>());

    [Test]
    public void One_that_isn_t_XML_is_refused() =>
        Assert.That(() => Read("not a gpx"), Throws.InstanceOf<InvalidDataException>());
}
