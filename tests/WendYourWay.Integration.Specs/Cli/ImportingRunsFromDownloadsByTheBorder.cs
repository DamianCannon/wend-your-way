using WendYourWay.Cli;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Outings;
using WendYourWay.Integration.Specs.Support;
using Npgsql;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend outings import-downloads` (PLAN T130): a run in Scotland among the files is skipped and
/// counted as outside England and Wales. On Fixtures/border.osm, an invented recording.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ImportingRunsFromDownloadsByTheBorder
{
    private string _downloads = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("border.osm"));
        await using (var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString))
            await NetworkBuild.Build(dataSource);
        _downloads = Directory.CreateTempSubdirectory("cutthrough-border-downloads-").FullName;
        var start = new DateTime(2025, 8, 9, 8, 0, 0, DateTimeKind.Utc);
        Fix[] inScotland = [.. Enumerable.Range(0, 40).Select(i => new Fix(55.38, -2.53 + (i * 0.0015), start.AddSeconds(15 * i)))];
        await File.WriteAllBytesAsync(Path.Combine(_downloads, "In Scotland.gpx"), TrackFiles.Gpx(inScotland, "running", "In Scotland"));
    }

    [OneTimeTearDown]
    public void Dispose() => Directory.Delete(_downloads, recursive: true);

    [Test]
    public async Task A_run_outside_England_and_Wales_is_counted_so()
    {
        using var output = new StringWriter();
        await CommandLine.Run(["outings", "import-downloads", "--folder", _downloads, "--database", TestDatabase.ConnectionString], output);
        Assert.That(output.ToString(), Does.Contain("No new runs to import. Skipped: 1 outside England and Wales."));
    }
}
