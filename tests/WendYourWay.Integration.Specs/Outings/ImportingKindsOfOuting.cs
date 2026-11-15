using System.Globalization;
using System.Text.RegularExpressions;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// An Outing keeps what it was (a run, walk, hike, ride or gravel ride) and the planning Activity
/// it informs: a walk or hike informs Run; a road ride is kept, since the user chose its way, but
/// informs neither Run nor Gravel Ride (user, 2026-10-18).
/// </summary>
[TestFixture]
[NonParallelizable]
public class ImportingKindsOfOuting
{
    private NpgsqlDataSource _dataSource = null!;
    private string _folder = null!;

    [OneTimeSetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _folder = Directory.CreateTempSubdirectory("cutthrough-kinds-").FullName;
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        _dataSource.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private static int _shift;

    //  The invented Run, as a GPX file of another type, on another day and moved a little, so it's
    //  a different Outing.
    private string GpxOfType(string type)
    {
        var day = ++_shift;
        var text = File.ReadAllText(Fixtures.Path("pavement-and-park.gpx"))
            .Replace("<type>running</type>", $"<type>{type}</type>", StringComparison.Ordinal)
            .Replace("2026-10-18T", $"2026-10-{18 + day}T", StringComparison.Ordinal);
        text = Regex.Replace(text, "lat=\"([0-9.]+)\"", m =>
            $"lat=\"{double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + (day * 0.0001):0.0000000}\"");
        var path = Path.Combine(_folder, $"{type}.gpx");
        File.WriteAllText(path, text);
        return path;
    }

    //  A file that doesn't say what it was (Footpath's, Wend Your Way's own) was skipped from
    //  2026-10-18; since T133 it's judged by its speed (user chose "Judge by speed").
    private static RecordedTrack Untyped(double kmPerHour, int minutes, int stoppedMinutes = 0)
    {
        var start = new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
        var degreesPerTenSeconds = kmPerHour / 3.6 * 10 / 111_320;
        var moving = Enumerable.Range(0, (minutes * 6) + 1).Select(i => new TrackPoint(51.5 + (i * degreesPerTenSeconds), -0.1, start.AddSeconds(10 * i))).ToList();
        //  Stopped half way, a point a minute where it stood (a café, a level crossing).
        var half = moving.Count / 2;
        var stood = Enumerable.Range(1, stoppedMinutes).Select(m => moving[half] with { Time = moving[half].Time!.Value.AddMinutes(m) });
        var after = moving.Skip(half + 1).Select(p => p with { Time = p.Time!.Value.AddMinutes(stoppedMinutes) });
        return new RecordedTrack("Untyped", null, [.. moving.Take(half + 1), .. stood, .. after]);
    }

    [TestCase(9.4, "run", Description = "the user's Trent Park run from Footpath")]
    [TestCase(14.5, "run")]
    [TestCase(15.5, "ride")]
    [TestCase(24, "ride", Description = "most of the user's cycling is on the road")]
    public void A_track_that_doesn_t_say_what_it_was_is_judged_by_its_speed(double kmPerHour, string kind) =>
        Assert.That(OutingImporter.KindOf(Untyped(kmPerHour, 60)), Is.EqualTo(kind));

    [Test]
    public void Time_stood_still_doesn_t_make_a_ride_a_run() =>
        Assert.That(OutingImporter.KindOf(Untyped(20, 60, stoppedMinutes: 45)), Is.EqualTo("ride"));

    [Test]
    public void One_with_too_few_times_to_tell_is_refused_and_says_why() =>
        Assert.That(() => OutingImporter.KindOf(new RecordedTrack("Untyped", null, [new(51.5, -0.1, null), new(51.501, -0.1, null)])),
            Throws.InstanceOf<InvalidDataException>().With.Message.Contains("doesn't say what it was"));

    [Test]
    public void One_of_a_kind_WendYourWay_doesn_t_know_is_still_refused() =>
        Assert.That(() => OutingImporter.KindOf(Untyped(2, 60) with { Type = "swimming" }),
            Throws.InstanceOf<InvalidDataException>().With.Message.Contains("'swimming'"));

    //  T133: a planned route is known as one before what it was is asked.
    [Test]
    public void A_planned_route_that_doesn_t_say_what_it_was_is_refused_as_a_planned_route()
    {
        var path = GpxOfType("planned");
        File.WriteAllText(path, Regex.Replace(File.ReadAllText(path).Replace("<type>planned</type>", "", StringComparison.Ordinal), "<time>[^<]*</time>", ""));
        Assert.That(async () => await OutingImporter.ImportGpxAsync(_dataSource, path),
            Throws.InstanceOf<InvalidDataException>().With.Message.Contains("planned route"));
    }

    [TestCase("running", "run", "run")]
    [TestCase("walking", "walk", "run")]
    [TestCase("hiking", "hike", "run")]
    [TestCase("cycling", "ride", null, Description = "most of the user's cycling is on the road")]
    [TestCase("mountain_biking", "gravel_ride", "gravel_ride")]
    [TestCase("jogging", "run", "run", Description = "komoot's name for a run (T133)")]
    [TestCase("mountaineering", "hike", "run")]
    [TestCase("touringbicycle", "ride", null)]
    [TestCase("racebike", "ride", null)]
    [TestCase("mtb_easy", "gravel_ride", "gravel_ride", Description = "komoot's gravel riding")]
    [TestCase("e_mtb", "gravel_ride", "gravel_ride")]
    public async Task An_Outing_keeps_what_it_was_and_the_Activity_it_informs(string type, string kind, string? activity)
    {
        await OutingImporter.ImportGpxAsync(_dataSource, GpxOfType(type));
        await using var query = _dataSource.CreateCommand("select kind::text, activity::text from app.outings where source_file = @file");
        query.Parameters.Add(new NpgsqlParameter("file", $"{type}.gpx"));
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.That((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)), Is.EqualTo((kind, activity)));
    }
}
