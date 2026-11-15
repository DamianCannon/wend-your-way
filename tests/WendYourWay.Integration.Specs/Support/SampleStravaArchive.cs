using System.IO.Compression;
using System.Text;
using Dynastream.Fit;

namespace WendYourWay.Integration.Specs.Support;

/// <summary>
/// A small Strava bulk export, as Strava lays it out, of invented recordings along road 200's north
/// pavement on Fixtures/same-ground.osm (by Gospel Oak).
/// </summary>
internal static class SampleStravaArchive
{
    //  East along the north pavement, a point every 5 m, starting at the given time. Each day's
    //  recording wanders a little differently, as real ones do.
    public static Fix[] AlongThePavement(System.DateTime start) =>
        [.. Enumerable.Range(0, 30).Select(i => new Fix(51.55505 + (start.Day * 0.000002), -0.1650 + (i * 0.00007), start.AddSeconds(2 * i)))];

    public static readonly System.DateTime RunStart = new(2024, 5, 4, 7, 30, 0, DateTimeKind.Utc);
    public static readonly System.DateTime WalkStart = new(2024, 5, 5, 10, 0, 0, DateTimeKind.Utc);
    public static readonly System.DateTime RideStart = new(2024, 5, 6, 9, 0, 0, DateTimeKind.Utc);
    public static readonly System.DateTime TrainerStart = new(2024, 5, 7, 18, 0, 0, DateTimeKind.Utc);
    public static readonly System.DateTime AlreadyStart = new(2024, 5, 8, 7, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Run 101 (GPX), walk 102 (gzipped TCX), road ride 103 (gzipped FIT), a ride on a trainer
    /// (104) and a swim (105) to skip, run 106 (also downloaded as its own GPX, which a spec may
    /// import first), and run 107 starting 30 s after 101.
    /// </summary>
    public static string Build()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("cutthrough-strava-").FullName, "export_1.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, byte[] content)
        {
            using var entry = zip.CreateEntry(name).Open();
            entry.Write(content);
        }

        //  As Strava writes it: quoted fields, a comma inside a name, some column names twice.
        Add("activities.csv", Encoding.UTF8.GetBytes("""
            Activity ID,Activity Date,Activity Name,Activity Type,Activity Description,Elapsed Time,Distance,Commute,Filename,Elapsed Time,Distance
            101,"May 4, 2024, 7:30:00 AM","Pavement, then home",Run,,60,0.15,false,activities/101.gpx,60,150
            102,"May 5, 2024, 10:00:00 AM",Sunday Walk,Walk,,60,0.15,false,activities/102.tcx.gz,60,150
            103,"May 6, 2024, 9:00:00 AM",Road Ride,Ride,,60,0.15,false,activities/103.fit.gz,60,150
            104,"May 7, 2024, 6:00:00 PM",Trainer,Virtual Ride,,3600,20,false,activities/104.fit.gz,3600,20000
            105,"May 7, 2024, 7:00:00 AM",Pool,Swim,,1800,1.5,false,,1800,1500
            106,"May 8, 2024, 7:00:00 AM",Already Here,Run,,60,0.15,false,activities/106.gpx,60,150
            107,"May 4, 2024, 7:30:30 AM",Second Watch,Run,,60,0.15,false,activities/107.gpx,60,150
            """.ReplaceLineEndings("\n")));
        Add("activities/101.gpx", TrackFiles.Gpx(AlongThePavement(RunStart), "9"));
        Add("activities/102.tcx.gz", TrackFiles.Tcx(AlongThePavement(WalkStart), "Other"));
        Add("activities/103.fit.gz", TrackFiles.Fit(AlongThePavement(RideStart), Sport.Cycling, SubSport.Road));
        Add("activities/104.fit.gz", TrackFiles.Fit(AlongThePavement(TrainerStart), Sport.Cycling, SubSport.VirtualActivity));
        //  The archive's copy of a run already imported from its own GPX: the same start, but the
        //  points a little different, as Strava's export and the original upload are.
        //  Another Strava activity starting 30 s after run 101 (a second watch, say): its own Outing.
        Add("activities/107.gpx", TrackFiles.Gpx(AlongThePavement(RunStart.AddSeconds(30)).Select(f => f with { Lon = f.Lon + 0.00001 }), "running"));
        Add("activities/106.gpx", TrackFiles.Gpx(AlongThePavement(AlreadyStart).Select(f => f with { Lat = f.Lat + 0.000001 }), "running"));

        //  Contacts, comments and photos sit beside them in a real archive, never to be read.
        Add("contacts.csv", Encoding.UTF8.GetBytes("Name,Email\nSomeone,someone@example.com\n"));
        return path;
    }
}
