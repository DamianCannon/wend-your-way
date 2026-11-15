using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using WendYourWay.Data.Planning;

namespace WendYourWay.Data.Outings;

/// <summary>
/// An Outing's own track as GPX 1.1 (2026-11-10): every point recorded, named for the Outing, to
/// overlay in OpenStreetMap's editor where it left the map ("precisely the type of route that I
/// want to overlay in OSM to see what's missing", user). The user's own recording, not drawn from
/// OSM, so no credit to it is needed.
/// </summary>
public static partial class OutingGpx
{
    private static readonly XNamespace Gpx = "http://www.topografix.com/GPX/1/1";

    public static GpxFile Of(string name, IReadOnlyList<LatLon> track)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(track);
        var document = new XDocument(
            new XElement(Gpx + "gpx",
                new XAttribute("version", "1.1"),
                new XAttribute("creator", "Wend Your Way"),
                new XElement(Gpx + "metadata", new XElement(Gpx + "name", name)),
                new XElement(Gpx + "trk",
                    new XElement(Gpx + "name", name),
                    new XElement(Gpx + "trkseg",
                        track.Select(p => new XElement(Gpx + "trkpt",
                            new XAttribute("lat", Degrees(p.Latitude)),
                            new XAttribute("lon", Degrees(p.Longitude))))))));

        // Written as UTF-8 bytes, so the declaration says UTF-8: the browser saves the text that way.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var bytes = new MemoryStream();
        using (var writer = XmlWriter.Create(bytes, new XmlWriterSettings { Indent = true, Encoding = utf8 }))
        {
            document.Save(writer);
        }
        return new GpxFile(FileNameOf(name), utf8.GetString(bytes.ToArray()));
    }

    // Its name, without what a file name can't hold on any system, its spaces single.
    private static string FileNameOf(string name)
    {
        var kept = Spaces().Replace(Unsafe().Replace(name, " "), " ").Trim();
        return (kept.Length == 0 ? "outing" : kept) + ".gpx";
    }

    [GeneratedRegex(@"[\\/:*?""<>|\x00-\x1f]")]
    private static partial Regex Unsafe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // Six decimal places is about 0.1 m, finer than a GPS fix.
    private static string Degrees(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
