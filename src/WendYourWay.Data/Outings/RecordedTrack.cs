using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Dynastream.Fit;

namespace WendYourWay.Data.Outings;

/// <summary>One recorded point of a track.</summary>
public readonly record struct TrackPoint(double Latitude, double Longitude, DateTimeOffset? Time);

/// <summary>
/// A run or ride as a file records it (GPX, TCX or FIT, from Strava's export or a watch): its
/// name and activity type as the file gives them, and its points in order. All tracks, laps and
/// segments in a file are read as one line.
/// </summary>
public sealed record RecordedTrack(string Name, string? Type, IReadOnlyList<TrackPoint> Points)
{
    /// <summary>When the track starts, if the file has timestamps.</summary>
    public DateTimeOffset? StartedAt => Points.Select(p => p.Time).FirstOrDefault(t => t is not null);

    /// <summary>Reads a GPX, TCX or FIT file, gzipped or not (as Strava's archive holds them).</summary>
    /// <exception cref="InvalidDataException">The file can't be read as a track; the message says why, without naming the file.</exception>
    public static RecordedTrack Read(string path)
    {
        using var file = System.IO.File.OpenRead(path);
        return Read(file, Path.GetFileName(path));
    }

    /// <inheritdoc cref="Read(string)"/>
    /// <param name="name">The file's name, which says its format: .gpx, .tcx or .fit, each optionally .gz.</param>
    public static RecordedTrack Read(Stream stream, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith(".gz", StringComparison.Ordinal))
        {
            using var unzipped = new GZipStream(stream, CompressionMode.Decompress);
            return Read(unzipped, name[..^3]);
        }

        var fallbackName = Path.GetFileNameWithoutExtension(name);
        var track = Path.GetExtension(lower) switch
        {
            ".gpx" => ReadGpx(stream, fallbackName),
            ".tcx" => ReadTcx(stream, fallbackName),
            ".fit" => ReadFit(stream, fallbackName),
            var other => throw new InvalidDataException($"it's a '{other}' file, not GPX, TCX or FIT"),
        };
        return track.Points.Count >= 2 ? track : throw new InvalidDataException("it has no GPS track to import");
    }

    private static RecordedTrack ReadGpx(Stream stream, string fallbackName)
    {
        var root = LoadXml(stream, "gpx");
        var gpx = root.Name.Namespace;
        var track = root.Elements(gpx + "trk").FirstOrDefault();
        var name = track?.Element(gpx + "name")?.Value.Trim()
            ?? root.Element(gpx + "metadata")?.Element(gpx + "name")?.Value.Trim();
        var type = track?.Element(gpx + "type")?.Value.Trim();

        List<TrackPoint> points = [.. root.Elements(gpx + "trk").Elements(gpx + "trkseg").Elements(gpx + "trkpt").Select(p => new TrackPoint(
            Number((string?)p.Attribute("lat")), Number((string?)p.Attribute("lon")), Time(p.Element(gpx + "time")?.Value)))];
        return new RecordedTrack(string.IsNullOrEmpty(name) ? fallbackName : name, type, points);
    }

    // Garmin's Training Center format: Activity Sport="Running"|"Biking"|"Other", Trackpoints in Laps.
    private static RecordedTrack ReadTcx(Stream stream, string fallbackName)
    {
        var root = LoadXml(stream, "TrainingCenterDatabase");
        var tcx = root.Name.Namespace;
        var activity = root.Element(tcx + "Activities")?.Element(tcx + "Activity");
        var sport = (string?)activity?.Attribute("Sport");

        List<TrackPoint> points = [.. (activity?.Descendants(tcx + "Trackpoint") ?? [])
            .Select(p => (Point: p, Position: p.Element(tcx + "Position")))
            .Where(p => p.Position is not null)
            .Select(p => new TrackPoint(
                Number(p.Position!.Element(tcx + "LatitudeDegrees")?.Value),
                Number(p.Position.Element(tcx + "LongitudeDegrees")?.Value),
                Time(p.Point.Element(tcx + "Time")?.Value)))];
        return new RecordedTrack(fallbackName, sport == "Biking" ? "cycling" : sport, points);
    }

    // Garmin's FIT, decoded with Garmin's own SDK: positions in semicircles, the sport in the session.
    private static RecordedTrack ReadFit(Stream stream, string fallbackName)
    {
        // The decoder checks the header and seeks, so it needs the whole file in memory.
        using var file = new MemoryStream();
        stream.CopyTo(file);
        file.Position = 0;

        var decode = new Decode();
        var broadcaster = new MesgBroadcaster();
        decode.MesgEvent += broadcaster.OnMesg;
        var points = new List<TrackPoint>();
        string? type = null;
        broadcaster.RecordMesgEvent += (_, e) =>
        {
            var record = (RecordMesg)e.mesg;
            if (record.GetPositionLat() is { } lat && record.GetPositionLong() is { } lon && HasPosition(lat, lon))
                points.Add(new TrackPoint(Degrees(lat), Degrees(lon),
                    record.GetTimestamp() is { } time ? new DateTimeOffset(time.GetDateTime(), TimeSpan.Zero) : null));
        };
        broadcaster.SessionMesgEvent += (_, e) => type ??= TypeOf((SessionMesg)e.mesg);

        try
        {
            if (!decode.IsFIT(file)) throw new InvalidDataException("it isn't a FIT file");
            file.Position = 0;
            decode.Read(file);
        }
        catch (FitException e)
        {
            throw new InvalidDataException($"it isn't a readable FIT file ({e.Message})", e);
        }
        return new RecordedTrack(fallbackName, type, points);
    }

    // A record with no fix (indoors, or before GPS locks on) carries FIT's "invalid" value,
    // 0x7FFFFFFF, or on some devices 0, 0.
    private static bool HasPosition(int lat, int lon) =>
        lat != int.MaxValue && lon != int.MaxValue && !(lat == 0 && lon == 0);

    // FIT stores angles as 32-bit semicircles: 2^31 of them make 180 degrees.
    private static double Degrees(int semicircles) => semicircles * (180.0 / 2_147_483_648.0);

    private static string? TypeOf(SessionMesg session) => (session.GetSport(), session.GetSubSport()) switch
    {
        (_, SubSport.VirtualActivity or SubSport.IndoorCycling or SubSport.IndoorRunning or SubSport.IndoorWalking) => "virtual",
        (Sport.Cycling, SubSport.GravelCycling or SubSport.Mountain or SubSport.Cyclocross or SubSport.EBikeMountain) => "gravel_cycling",
        (Sport.Cycling, _) => "cycling",
        (Sport.Running, _) => "running",
        (Sport.Walking, _) => "walking",
        (Sport.Hiking, _) => "hiking",
        (var sport, _) => sport?.ToString().ToLowerInvariant(),
    };

    private static XElement LoadXml(Stream stream, string rootName)
    {
        // No DTDs and no external entities: a track file is data from elsewhere. Strava's TCX
        // files start with spaces before the XML declaration, which XML forbids, so they go.
        using var text = new StreamReader(stream);
        var content = text.ReadToEnd().TrimStart();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(content), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException e)
        {
            throw new InvalidDataException($"it isn't readable XML ({e.Message})", e);
        }
        return document.Root is { } root && root.Name.LocalName == rootName
            ? root
            : throw new InvalidDataException($"it isn't a {rootName} file");
    }

    private static double Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException("a point has no position");

    private static DateTimeOffset? Time(string? text) =>
        text is null ? null
        : DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time
        : throw new InvalidDataException($"a point's time ('{text}') isn't a time");
}
