using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WendYourWay.Core.Loops;

namespace WendYourWay.Data.Planning;

/// <summary>
/// A planned Loop as a TCX course (PLAN T100, STORY-043), which watches read for turn prompts: its
/// line as the course's track, timed as if run at an even 10 km/h (a course needs times), and each
/// turn point a course point, Left, Right or Generic, its words as notes. Names kept within
/// Garmin's limits: 15 characters for a course, 10 for a course point.
/// </summary>
public static class LoopTcx
{
    private static readonly XNamespace Tcx = "http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2";

    // An even 10 km/h, from a fixed start: only the order and spacing of the times matter.
    private const double MetresPerSecond = 10_000 / 3600.0;
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <param name="name">The route's own name (T112), the file and course named from it; none, by its length.</param>
    public static GpxFile Of(PlannedLoop loop, IReadOnlyList<RouteTurn> turns, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(turns);
        var km = (loop.Breakdown.LengthM / 1000).ToString("0.0", CultureInfo.InvariantCulture);
        var own = ExportNames.Usable(name);

        // Along the line, in metres, each point at least a second after the one before.
        var along = new double[loop.Line.Count];
        for (var i = 1; i < along.Length; i++) along[i] = along[i - 1] + Metres(loop.Line[i - 1], loop.Line[i]);
        var seconds = new double[along.Length];
        for (var i = 0; i < along.Length; i++) seconds[i] = Math.Max(along[i] / MetresPerSecond, i == 0 ? 0 : seconds[i - 1] + 1);

        var document = new XDocument(
            new XElement(Tcx + "TrainingCenterDatabase",
                new XElement(Tcx + "Courses",
                    new XElement(Tcx + "Course",
                        new XElement(Tcx + "Name", Fit(own ?? $"Wend {km} km", 15)),
                        new XElement(Tcx + "Lap",
                            new XElement(Tcx + "TotalTimeSeconds", Number(seconds.Length == 0 ? 0 : seconds[^1])),
                            new XElement(Tcx + "DistanceMeters", Number(along.Length == 0 ? 0 : along[^1])),
                            Position("BeginPosition", loop.Line[0]),
                            Position("EndPosition", loop.Line[^1]),
                            new XElement(Tcx + "Intensity", "Active")),
                        new XElement(Tcx + "Track",
                            loop.Line.Select((p, i) => new XElement(Tcx + "Trackpoint",
                                new XElement(Tcx + "Time", Time(seconds[i])),
                                Position("Position", p),
                                new XElement(Tcx + "DistanceMeters", Number(along[i]))))),
                        turns.Select(turn => new XElement(Tcx + "CoursePoint",
                            new XElement(Tcx + "Name", Fit(Short(turn.Kind), 10)),
                            new XElement(Tcx + "Time", Time(turn.AlongM / MetresPerSecond)),
                            Position("Position", turn.At),
                            new XElement(Tcx + "PointType", PointType(turn.Kind)),
                            new XElement(Tcx + "Notes", turn.Words)))))));

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var bytes = new MemoryStream();
        using (var writer = XmlWriter.Create(bytes, new XmlWriterSettings { Indent = true, Encoding = utf8 }))
        {
            document.Save(writer);
        }
        return new GpxFile(own is null ? $"wend-your-way-{km}km.tcx" : ExportNames.FileName(own, "tcx"), utf8.GetString(bytes.ToArray()));
    }

    private static XElement Position(string name, LatLon at) =>
        new(Tcx + name,
            new XElement(Tcx + "LatitudeDegrees", at.Latitude.ToString("0.######", CultureInfo.InvariantCulture)),
            new XElement(Tcx + "LongitudeDegrees", at.Longitude.ToString("0.######", CultureInfo.InvariantCulture)));

    private static string Time(double seconds) => Start.AddSeconds(seconds).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    // Cut to fit, without a dangling separator ("Kenwood House ·" is "Kenwood House").
    private static string Fit(string text, int most) => text.Length <= most ? text : text[..most].TrimEnd(' ', '·', '-', ',', ':');

    private static string Short(TurnKind kind) => kind switch
    {
        TurnKind.Left => "Left",
        TurnKind.Right => "Right",
        TurnKind.SharpLeft => "Sharp L",
        TurnKind.SharpRight => "Sharp R",
        TurnKind.MapGap => "Map gap",
        _ => "Turn back",
    };

    private static string PointType(TurnKind kind) => kind switch
    {
        TurnKind.Left or TurnKind.SharpLeft => "Left",
        TurnKind.Right or TurnKind.SharpRight => "Right",
        // A Map gap (T146b): somewhere to look before going on.
        TurnKind.MapGap => "Danger",
        _ => "Generic",
    };

    // Near enough for timing: a flat Earth at the latitude between them.
    private static double Metres(LatLon a, LatLon b)
    {
        const double MetresPerDegree = 111_320;
        var north = (a.Latitude - b.Latitude) * MetresPerDegree;
        var east = (a.Longitude - b.Longitude) * MetresPerDegree * Math.Cos((a.Latitude + b.Latitude) / 2 * Math.PI / 180);
        return Math.Sqrt(north * north + east * east);
    }
}
