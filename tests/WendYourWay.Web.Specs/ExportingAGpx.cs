using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// STORY-029: a Loop exported as GPX 1.1 to follow in the OS Maps app. Public landmarks only.
/// </summary>
public class ExportingAGpx
{
    private static readonly XNamespace Gpx = "http://www.topografix.com/GPX/1/1";

    //  A 9.71 km Loop from Kenwood House round Parliament Hill and back.
    private static readonly PlannedLoop Planned = new(
        Line: [new(51.5712, -0.1677), new(51.5655, -0.1641), new(51.5597, -0.1607), new(51.5650, -0.1500), new(51.5712, -0.1677)],
        Start: new(51.5712, -0.1677),
        Anchors: [new(51.5597, -0.1607), new(51.5650, -0.1500)],
        Breakdown: new Breakdown(LengthM: 9710, OffRoadPercent: 62, TrafficFreePercent: 17, ConnectorLengthM: 1990, RetraceLengthM: 340));

    [Test]
    public void Then_it_is_valid_GPX_1_1()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(Gpx.NamespaceName, Path.Combine(TestContext.CurrentContext.TestDirectory, "Schemas", "gpx-1.1.xsd"));
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        var problems = new List<string>();
        settings.ValidationEventHandler += (_, e) => problems.Add($"{e.Severity}: {e.Message}");

        using (var reader = XmlReader.Create(new StringReader(LoopGpx.Of(Planned).Content), settings))
        {
            while (reader.Read()) { }
        }
        Assert.That(problems, Is.Empty);
    }

    //  The browser saves the text as UTF-8, so the file must say so: a .NET string writer would
    //  otherwise declare UTF-16, which apps reading the bytes may refuse.
    [Test]
    public void Then_it_declares_itself_UTF_8() =>
        Assert.That(LoopGpx.Of(Planned).Content, Does.StartWith("""<?xml version="1.0" encoding="utf-8"?>"""));

    private static List<LatLon> TrackPoints(string gpx) =>
        [.. XDocument.Parse(gpx).Root!.Elements(Gpx + "trk").Single().Elements(Gpx + "trkseg").Single().Elements(Gpx + "trkpt")
            .Select(p => new LatLon(double.Parse(p.Attribute("lat")!.Value, CultureInfo.InvariantCulture),
                                    double.Parse(p.Attribute("lon")!.Value, CultureInfo.InvariantCulture)))];

    //  One track of one segment: OS Maps imports it as one route.
    [Test]
    public void Then_its_one_track_follows_the_Loop_point_by_point_in_order() =>
        Assert.That(TrackPoints(LoopGpx.Of(Planned).Content), Is.EqualTo(Planned.Line));

    //  How it shows in the OS Maps library, and in Downloads.
    [Test]
    public void Then_it_is_named_by_its_length() =>
        Assert.That(XDocument.Parse(LoopGpx.Of(Planned).Content).Root!.Element(Gpx + "trk")!.Element(Gpx + "name")!.Value,
            Is.EqualTo("Wend Your Way 9.7 km Loop"));

    //  T092: a route, ending over 200 m from its start, is named a route.
    [Test]
    public void Then_a_route_is_named_a_route() =>
        Assert.That(XDocument.Parse(LoopGpx.Of(Planned with { Line = [.. Planned.Line.SkipLast(1)] }).Content).Root!.Element(Gpx + "trk")!.Element(Gpx + "name")!.Value,
            Is.EqualTo("Wend Your Way 9.7 km route"));

    [Test]
    public void Then_its_file_is_named_by_its_length() =>
        Assert.That(LoopGpx.Of(Planned).FileName, Is.EqualTo("wend-your-way-9.7km.gpx"));

    //  T112 (user: "I'd like the names used when exporting files to be more useful than just saying
    //  Wend Your Way and the length!"; chose "Its own name, else place"): named as the route is.
    [Test]
    public void Then_it_carries_the_route_s_own_name()
    {
        var gpx = XDocument.Parse(LoopGpx.Of(Planned, name: "Heath figure of eight").Content).Root!;
        Assert.That((gpx.Element(Gpx + "metadata")!.Element(Gpx + "name")!.Value, gpx.Element(Gpx + "trk")!.Element(Gpx + "name")!.Value),
            Is.EqualTo(("Heath figure of eight", "Heath figure of eight")));
    }

    [TestCase("Heath figure of eight", "heath-figure-of-eight.gpx")]
    [TestCase("Kenwood House · 9.7 km", "kenwood-house-9.7km.gpx")]
    [TestCase("Kenwood House to Parliament Hill · 12.0 km", "kenwood-house-to-parliament-hill-12.0km.gpx")]
    [TestCase("Finsbury Park Run", "finsbury-park-run.gpx")]
    [TestCase("  Up/down: the 'Heath'!  ", "up-down-the-heath.gpx")]
    [TestCase("Café run", "café-run.gpx")]
    public void Then_its_file_is_named_from_it(string name, string file) =>
        Assert.That(LoopGpx.Of(Planned, name: name).FileName, Is.EqualTo(file));

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("·")]
    public void Then_with_no_name_to_use_it_is_named_by_its_length(string name) =>
        Assert.That((LoopGpx.Of(Planned, name: name).FileName, XDocument.Parse(LoopGpx.Of(Planned, name: name).Content).Root!.Element(Gpx + "trk")!.Element(Gpx + "name")!.Value),
            Is.EqualTo(("wend-your-way-9.7km.gpx", "Wend Your Way 9.7 km Loop")));

    //  The line is drawn from OpenStreetMap, whose licence (ODbL) asks for credit wherever it goes.
    [Test]
    public void Then_it_credits_OpenStreetMap()
    {
        var copyright = XDocument.Parse(LoopGpx.Of(Planned).Content).Root!.Element(Gpx + "metadata")!.Element(Gpx + "copyright")!;
        Assert.That((copyright.Attribute("author")!.Value, copyright.Element(Gpx + "license")!.Value),
            Is.EqualTo(("OpenStreetMap contributors", "https://opendatacommons.org/licenses/odbl/")));
    }
}
