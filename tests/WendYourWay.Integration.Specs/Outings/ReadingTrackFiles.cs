using WendYourWay.Data.Outings;
using WendYourWay.Integration.Specs.Support;
using Dynastream.Fit;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// Strava's archive holds tracks as GPX, gzipped TCX and gzipped FIT (Garmin's binary format). Each
/// is read into the same track. Positions are invented, on Hampstead Heath.
/// </summary>
public class ReadingTrackFiles
{
    private static readonly System.DateTime Start = new(2024, 5, 4, 7, 30, 0, DateTimeKind.Utc);

    private static readonly Fix[] Heath =
        [new(51.5597, -0.1607, Start), new(51.5605, -0.1615, Start.AddSeconds(1)), new(51.5612, -0.1624, Start.AddSeconds(2))];

    private static void AssertTheHeath(RecordedTrack track)
    {
        Assert.That(track.Points, Has.Count.EqualTo(Heath.Length), "points with a position");
        for (var i = 0; i < Heath.Length; i++)
        {
            Assert.That(track.Points[i].Latitude, Is.EqualTo(Heath[i].Lat).Within(1e-6), $"point {i}");
            Assert.That(track.Points[i].Longitude, Is.EqualTo(Heath[i].Lon).Within(1e-6), $"point {i}");
        }
        Assert.That(track.StartedAt, Is.EqualTo(new DateTimeOffset(Start)));
    }

    [Test]
    public void A_gzipped_TCX_file_as_Strava_writes_it_is_read()
    {
        var track = RecordedTrack.Read(new MemoryStream(TrackFiles.Tcx(Heath, "Running")), "12345.tcx.gz");
        AssertTheHeath(track);
        Assert.That(track.Type, Is.EqualTo("Running"));
    }

    [Test]
    public void A_gzipped_FIT_file_is_read()
    {
        var track = RecordedTrack.Read(new MemoryStream(TrackFiles.Fit(Heath, Sport.Running, SubSport.Trail)), "12345.fit.gz");
        AssertTheHeath(track);
        Assert.That(track.Type, Is.EqualTo("running"));
    }

    //  So the importer can tell a gravel ride from a road ride, and drop a ride on a trainer.
    [TestCase(Sport.Cycling, SubSport.Road, "cycling")]
    [TestCase(Sport.Cycling, SubSport.GravelCycling, "gravel_cycling")]
    [TestCase(Sport.Cycling, SubSport.VirtualActivity, "virtual")]
    public void A_FIT_file_says_what_kind_of_ride_it_was(Sport sport, SubSport subSport, string type) =>
        Assert.That(RecordedTrack.Read(new MemoryStream(TrackFiles.Fit(Heath, sport, subSport)), "ride.fit.gz").Type, Is.EqualTo(type));
}
