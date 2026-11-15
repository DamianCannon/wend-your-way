using System.Xml.Linq;
using System.Xml.Schema;
using WendYourWay.Data;
using WendYourWay.Data.Outings;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// An Outing's own track as GPX 1.1 (2026-11-10): every point recorded, named for the Outing, to
/// overlay in OpenStreetMap's editor where it left the map. Valid GPX 1.1, by the schema.
/// </summary>
public class WritingAnOutingAsGpx
{
    private static readonly LatLon[] Track = [new(51.7001, -0.2101), new(51.7002, -0.2102), new(51.7003, -0.2103)];
    private static readonly XNamespace Gpx = "http://www.topografix.com/GPX/1/1";

    [Test]
    public void Every_point_it_recorded_is_in_its_track() =>
        Assert.That(XDocument.Parse(OutingGpx.Of("Route finding", Track).Content).Descendants(Gpx + "trkpt").Count(), Is.EqualTo(3));

    [Test]
    public void It_is_named_for_the_Outing() =>
        Assert.That((OutingGpx.Of("Route finding / in the park", Track).FileName, XDocument.Parse(OutingGpx.Of("Route finding", Track).Content).Descendants(Gpx + "name").First().Value),
            Is.EqualTo(("Route finding in the park.gpx", "Route finding")));

    [Test]
    public void It_is_valid_GPX_1_1()
    {
        var schemas = new System.Xml.Schema.XmlSchemaSet();
        schemas.Add("http://www.topografix.com/GPX/1/1", Path.Combine(TestContext.CurrentContext.TestDirectory, "Schemas", "gpx-1.1.xsd"));
        var errors = new List<string>();
        XDocument.Parse(OutingGpx.Of("Route finding", Track).Content).Validate(schemas, (_, e) => errors.Add(e.Message));
        Assert.That(errors, Is.Empty);
    }
}
