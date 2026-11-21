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
    /// <param name="activity">What it's for (T159): how far before its turn each turn's waypoint is put.</param>
    public static GpxFile Of(PlannedLoop loop, IReadOnlyList<RouteTurn>? turns = null, string? name = null, Activity activity = Activity.Run)
    {
        ArgumentNullException.ThrowIfNull(loop);
        turns ??= [];
        var heard = HeardAt(loop.Line, turns, LeadM(activity));

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
                turns.Select((turn, i) => new XElement(Gpx + "wpt",
                    new XAttribute("lat", Degrees(heard[i].Latitude)),
                    new XAttribute("lon", Degrees(heard[i].Longitude)),
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

    /// <summary>
    /// How far before its turn a turn's waypoint goes (T159, user: "the waypoint was spoken after I'd
    /// actually turned right and gone over it"): an app speaks a waypoint as it's reached, so about
    /// ten seconds' going before it, at a run's pace or a ride's.
    /// </summary>
    public static double LeadM(Activity activity) => activity == Activity.Run ? 30 : 50;

    // Where each turn's waypoint goes: back along the line from its junction by the lead, but no more
    // than half the way to the turn before it, or to the start, so they're heard in order. A Map gap's
    // stays where the gap is; so does a turn not found on the line.
    private static LatLon[] HeardAt(IReadOnlyList<LatLon> line, IReadOnlyList<RouteTurn> turns, double leadM)
    {
        var along = new double[line.Count];
        for (var i = 1; i < line.Count; i++) along[i] = along[i - 1] + line[i - 1].MetresTo(line[i]);
        var heard = new LatLon[turns.Count];
        var before = 0.0;
        for (var t = 0; t < turns.Count; t++)
        {
            var turn = turns[t];
            heard[t] = turn.At;
            // The junction is a point of the line; where the line passes it twice, the pass nearest its distance along.
            var at = Enumerable.Range(0, line.Count).Where(i => line[i].MetresTo(turn.At) < 1)
                .OrderBy(i => Math.Abs(along[i] - turn.AlongM)).Cast<int?>().FirstOrDefault();
            if (at is not { } k) continue;
            var back = Math.Min(leadM, (along[k] - before) / 2);
            before = along[k];
            if (turn.Kind == TurnKind.MapGap || back <= 0) continue;
            heard[t] = PointAt(line, along, along[k] - back);
        }
        return heard;
    }

    // The point of the line this far along it.
    private static LatLon PointAt(IReadOnlyList<LatLon> line, double[] along, double metres)
    {
        var j = Array.FindLastIndex(along, a => a <= metres);
        if (j < 0) return line[0];
        if (j >= line.Count - 1) return line[^1];
        var share = along[j + 1] > along[j] ? (metres - along[j]) / (along[j + 1] - along[j]) : 0;
        return new LatLon(line[j].Latitude + ((line[j + 1].Latitude - line[j].Latitude) * share),
            line[j].Longitude + ((line[j + 1].Longitude - line[j].Longitude) * share));
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
