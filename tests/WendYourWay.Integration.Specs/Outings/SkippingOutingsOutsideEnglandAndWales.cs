using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Integration.Specs.Support;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// STORY-005 AC5, R6.1 (PLAN T030): an Outing that never enters England or Wales isn't
/// imported, since the Network doesn't cover where it went. On Fixtures/border.osm, with invented
/// recordings along the way in England and the track in Scotland.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SkippingOutingsOutsideEnglandAndWales
{
    private NpgsqlDataSource _dataSource = null!;
    private string _folder = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("border.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _folder = Directory.CreateTempSubdirectory("cutthrough-border-outings-").FullName;
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        _dataSource.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    //  East along a way, a point every 50 m or so, from the given time.
    private static Fix[] Along(double latitude, System.DateTime start) =>
        [.. Enumerable.Range(0, 40).Select(i => new Fix(latitude, -2.53 + (i * 0.0015), start.AddSeconds(15 * i)))];

    private static readonly System.DateTime InScotlandStart = new(2025, 8, 9, 8, 0, 0, DateTimeKind.Utc);

    private async Task<string> Gpx(string name, Fix[] fixes)
    {
        var path = Path.Combine(_folder, name);
        await System.IO.File.WriteAllBytesAsync(path, TrackFiles.Gpx(fixes, "running", name));
        return path;
    }

    private async Task<long> Outings()
    {
        await using var count = _dataSource.CreateCommand("select count(*) from app.outings");
        return (long)(await count.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task Then_a_run_in_Scotland_is_refused_and_says_why()
    {
        var before = await Outings();
        var gpx = await Gpx("In_Scotland.gpx", Along(55.38, InScotlandStart));
        Assert.That(async () => await OutingImporter.ImportGpxAsync(_dataSource, gpx),
            Throws.InstanceOf<InvalidDataException>().With.Message.Contains("outside England and Wales"));
        Assert.That(await Outings(), Is.EqualTo(before));
    }

    //  A Strava archive of a run in England (201) and a run in Scotland (202).
    private string Archive()
    {
        var path = Path.Combine(_folder, "export_border.zip");
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        void Add(string name, byte[] content)
        {
            using var entry = zip.CreateEntry(name).Open();
            entry.Write(content);
        }
        Add("activities.csv", System.Text.Encoding.UTF8.GetBytes("""
            Activity ID,Activity Date,Activity Name,Activity Type,Filename
            201,"Aug 10, 2025, 8:00:00 AM",Over the Cheviots,Run,activities/201.gpx
            202,"Aug 11, 2025, 8:00:00 AM",Up in the Borders,Run,activities/202.gpx
            """.ReplaceLineEndings("\n")));
        Add("activities/201.gpx", TrackFiles.Gpx(Along(55.32, new System.DateTime(2025, 8, 10, 8, 0, 0, DateTimeKind.Utc)), "running"));
        Add("activities/202.gpx", TrackFiles.Gpx(Along(55.38, new System.DateTime(2025, 8, 11, 8, 0, 0, DateTimeKind.Utc)), "running"));
        return path;
    }

    [Test]
    public async Task Then_an_archive_s_runs_in_Scotland_are_counted_as_outside_and_the_rest_imported()
    {
        var import = await StravaImporter.ImportAsync(_dataSource, Archive());
        Assert.That((import.Imported, import.OutsideEnglandAndWales, import.NotRead.Count), Is.EqualTo((1, 1, 0)));
    }
}
