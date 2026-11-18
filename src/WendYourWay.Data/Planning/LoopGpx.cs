using WendYourWay.Core.Loops;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace WendYourWay.Data.Planning;

/// <summary>A file to hand the browser: its name and its text.</summary>
public sealed record GpxFile(string FileName, string Content);

/// <summary>
/// A planned Loop as GPX 1.1, to follow in the OS Maps app (STORY-029): one track of one
/// segment, its line in travel order from the start round to the start again.
/// </summary>
public static class LoopGpx
{
    private static readonly XNamespace Gpx = "http://www.topografix.com/GPX/1/1";

    /// <param name="turns">Its turn points (T100), each a waypoint: its words as the name, Left or Right its symbol.</param>
    /// <param name="name">The route's own name (T112), the file named from it; none, by its length.</param>
    public static GpxFile Of(PlannedLoop loop, IReadOnlyList<RouteTurn>? turns = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(loop);

        var km = (loop.Breakdown.LengthM / 1000).ToString("0.0", CultureInfo.InvariantCulture);
        var own = ExportNames.Usable(name);
        // A route not back at its start (T092) is named so.
        name = own ?? $"Wend Your Way {km} km {(loop.EndsAtStart ? "Loop" : "route")}";
        var document = new XDocument(
            new XElement(Gpx + "gpx",
                new XAttribute("version", "1.1"),
                new XAttribute("creator", "Wend Your Way"),
                new XElement(Gpx + "metadata",
                    new XElement(Gpx + "name", name),
                    // The line is drawn from OpenStreetMap, whose licence asks for credit wherever it goes.
                    new XElement(Gpx + "copyright",
                        new XAttribute("author", "OpenStreetMap contributors"),
                        new XElement(Gpx + "license", "https://opendatacommons.org/licenses/odbl/"))),
                (turns ?? []).Select(turn => new XElement(Gpx + "wpt",
                    new XAttribute("lat", Degrees(turn.At.Latitude)),
                    new XAttribute("lon", Degrees(turn.At.Longitude)),
                    new XElement(Gpx + "name", turn.Words),
                    new XElement(Gpx + "sym", Symbol(turn.Kind)),
                    new XElement(Gpx + "type", turn.Kind == TurnKind.MapGap ? "Map gap" : "Turn"))),
                new XElement(Gpx + "trk",
                    new XElement(Gpx + "name", name),
                    new XElement(Gpx + "trkseg",
                        loop.Line.Select(p => new XElement(Gpx + "trkpt",
                            new XAttribute("lat", Degrees(p.Latitude)),
                            new XAttribute("lon", Degrees(p.Longitude))))))));

        // Written as UTF-8 bytes, so the declaration says UTF-8: the browser saves the text that way.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var bytes = new MemoryStream();
        using (var writer = XmlWriter.Create(bytes, new XmlWriterSettings { Indent = true, Encoding = utf8 }))
        {
            document.Save(writer);
        }
        return new GpxFile(own is null ? $"wend-your-way-{km}km.gpx" : ExportNames.FileName(own, "gpx"), utf8.GetString(bytes.ToArray()));
    }

    /// <summary>A turn's symbol, as apps that show waypoints know them.</summary>
    public static string Symbol(TurnKind kind) => kind switch
    {
        TurnKind.Left or TurnKind.SharpLeft => "Left",
        TurnKind.Right or TurnKind.SharpRight => "Right",
        // A Map gap (T146b): somewhere to look before going on.
        TurnKind.MapGap => "Danger Area",
        _ => "Generic",
    };

    // Six decimal places is about 0.1 m, far finer than the Network.
    private static string Degrees(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
