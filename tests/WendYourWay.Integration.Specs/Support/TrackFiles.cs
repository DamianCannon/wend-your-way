using System.Globalization;
using System.IO.Compression;
using System.Text;
using Dynastream.Fit;

namespace WendYourWay.Integration.Specs.Support;

/// <summary>A point of an invented recording: where, and when.</summary>
internal readonly record struct Fix(double Lat, double Lon, System.DateTime Time);

/// <summary>
/// Writes invented recordings in each format Strava's archive holds: GPX, gzipped TCX (with
/// Strava's stray leading spaces) and gzipped FIT, made with Garmin's own encoder.
/// </summary>
internal static class TrackFiles
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static byte[] Gzip(byte[] content)
    {
        using var zipped = new MemoryStream();
        using (var gzip = new GZipStream(zipped, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(content);
        return zipped.ToArray();
    }

    public static byte[] Gpx(IEnumerable<Fix> fixes, string type, string name = "Invented") => Encoding.UTF8.GetBytes($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx creator="StravaGPX" version="1.1" xmlns="http://www.topografix.com/GPX/1/1">
         <trk><name>{name}</name><type>{type}</type><trkseg>
        {string.Concat(fixes.Select(f => string.Format(Invariant, "  <trkpt lat=\"{0:0.0000000}\" lon=\"{1:0.0000000}\"><time>{2:yyyy-MM-ddTHH:mm:ssZ}</time></trkpt>\n", f.Lat, f.Lon, f.Time)))}
         </trkseg></trk>
        </gpx>
        """);

    //  Gzipped, with the spaces Strava puts before the XML declaration, which strict XML refuses,
    //  and a last Trackpoint with no position, as when GPS drops out.
    public static byte[] Tcx(IEnumerable<Fix> fixes, string sport) => Gzip(Encoding.UTF8.GetBytes($"""
                  <?xml version="1.0" encoding="UTF-8"?>
        <TrainingCenterDatabase xmlns="http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2">
          <Activities><Activity Sport="{sport}"><Lap><Track>
        {string.Concat(fixes.Select(f => string.Format(Invariant, "<Trackpoint><Time>{2:yyyy-MM-ddTHH:mm:ssZ}</Time><Position><LatitudeDegrees>{0}</LatitudeDegrees><LongitudeDegrees>{1}</LongitudeDegrees></Position></Trackpoint>\n", f.Lat, f.Lon, f.Time)))}
            <Trackpoint><Time>2024-05-04T09:00:00Z</Time><HeartRateBpm><Value>140</Value></HeartRateBpm></Trackpoint>
          </Track></Lap></Activity></Activities>
        </TrainingCenterDatabase>
        """));

    private static int Semicircles(double degrees) => (int)Math.Round(degrees * (2_147_483_648.0 / 180.0));

    //  Gzipped: the points, then a record with no position (as indoors), then the session saying
    //  what sport it was.
    public static byte[] Fit(IReadOnlyList<Fix> fixes, Sport sport, SubSport subSport)
    {
        using var file = new MemoryStream();
        var encoder = new Encode(file, ProtocolVersion.V20);
        var fileId = new FileIdMesg();
        fileId.SetType(Dynastream.Fit.File.Activity);
        fileId.SetManufacturer(Manufacturer.Development);
        fileId.SetTimeCreated(new Dynastream.Fit.DateTime(fixes[0].Time));
        encoder.Write(fileId);
        foreach (var fix in fixes)
        {
            var record = new RecordMesg();
            record.SetTimestamp(new Dynastream.Fit.DateTime(fix.Time));
            record.SetPositionLat(Semicircles(fix.Lat));
            record.SetPositionLong(Semicircles(fix.Lon));
            encoder.Write(record);
        }
        var indoors = new RecordMesg();
        indoors.SetTimestamp(new Dynastream.Fit.DateTime(fixes[^1].Time.AddSeconds(1)));
        encoder.Write(indoors);
        var session = new SessionMesg();
        session.SetStartTime(new Dynastream.Fit.DateTime(fixes[0].Time));
        session.SetSport(sport);
        session.SetSubSport(subSport);
        encoder.Write(session);
        encoder.Close();
        return Gzip(file.ToArray());
    }
}
