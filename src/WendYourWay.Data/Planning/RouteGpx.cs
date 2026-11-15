using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace WendYourWay.Data.Planning;

/// <summary>
/// A GPX route to import (T083): its name, if it has one, and its points in order: its track's
/// (<c>trkpt</c>), or if it has none its route's (<c>rtept</c>). Only for importing a route: an
/// Outing is read as a track (<see cref="Outings.RecordedTrack"/>), never from route points.
/// </summary>
public sealed record RouteGpx(string? Name, IReadOnlyList<LatLon> Points)
{
    /// <exception cref="InvalidDataException">It can't be read as a GPX, or has no points; the message says which.</exception>
    public static RouteGpx Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        XDocument document;
        try
        {
            // No DTD: a GPX has none, and refusing one means no entity in the file is expanded and
            // nothing outside it is fetched (XDocument.Load alone would expand them).
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new InvalidDataException("That file isn't a GPX: it can't be read.");
        }
        if (document.Root is not { } root || root.Name.LocalName != "gpx") throw new InvalidDataException("That file isn't a GPX.");
        var gpx = root.Name.Namespace;

        var tracks = root.Elements(gpx + "trk").ToArray();
        var routes = root.Elements(gpx + "rte").ToArray();
        var points = PointsOf(tracks.Elements(gpx + "trkseg").Elements(gpx + "trkpt"));
        if (points.Count == 0) points = PointsOf(routes.Elements(gpx + "rtept"));
        if (points.Count == 0) throw new InvalidDataException("That GPX has no route in it.");

        var name = new[]
        {
            tracks.Elements(gpx + "name").FirstOrDefault()?.Value,
            routes.Elements(gpx + "name").FirstOrDefault()?.Value,
            root.Element(gpx + "metadata")?.Element(gpx + "name")?.Value,
        }.Select(n => n?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n));
        return new RouteGpx(name, points);
    }

    private static List<LatLon> PointsOf(IEnumerable<XElement> points) =>
        [.. points
            .Select(p => (Lat: Number((string?)p.Attribute("lat")), Lon: Number((string?)p.Attribute("lon"))))
            .Where(p => p.Lat is >= -90 and <= 90 && p.Lon is >= -180 and <= 180)
            .Select(p => new LatLon(p.Lat!.Value, p.Lon!.Value))];

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
}
