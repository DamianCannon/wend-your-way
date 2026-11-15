using WendYourWay.Integration.Specs.Support;
using WendYourWay.Cli;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>`wend outings import-gpx &lt;file-or-folder&gt;` (PLAN T016, STORY-030 AC1).</summary>
[TestFixture]
[NonParallelizable]
public class ImportingOutingsFromTheCommandLine
{
    private string _folder = null!;
    private (int ExitCode, string Output) _first;
    private (int ExitCode, string Output) _again;

    private static async Task<(int ExitCode, string Output)> Ct(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(args, output);
        return (exitCode, output.ToString());
    }

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        var build = await Ct("network", "build", Fixtures.Path("same-ground.osm"),
            "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"), "--osm2pgsql", Tools.Osm2pgsql);
        Assert.That(build.ExitCode, Is.Zero, build.Output);

        //  A folder with the invented Run, a swim, which isn't something Wend Your Way plans, and a
        //  walk and a ride in the other formats Strava keeps: gzipped FIT and TCX.
        _folder = Directory.CreateTempSubdirectory("cutthrough-outings-").FullName;
        File.Copy(Fixtures.Path("pavement-and-park.gpx"), Path.Combine(_folder, "pavement-and-park.gpx"));
        File.WriteAllText(Path.Combine(_folder, "swim.gpx"),
            File.ReadAllText(Fixtures.Path("pavement-and-park.gpx")).Replace("<type>running</type>", "<type>swimming</type>", StringComparison.Ordinal));
        File.WriteAllBytes(Path.Combine(_folder, "walk.fit.gz"), TrackFiles.Fit(
            SampleStravaArchive.AlongThePavement(SampleStravaArchive.WalkStart), Dynastream.Fit.Sport.Walking, Dynastream.Fit.SubSport.Generic));
        File.WriteAllBytes(Path.Combine(_folder, "ride.tcx.gz"), TrackFiles.Tcx(SampleStravaArchive.AlongThePavement(SampleStravaArchive.RideStart), "Biking"));

        _first = await Ct("outings", "import-gpx", _folder, "--database", TestDatabase.ConnectionString);
        _again = await Ct("outings", "import-gpx", _folder, "--database", TestDatabase.ConnectionString);
    }

    [OneTimeTearDown]
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void Then_it_succeeds() => Assert.That(_first.ExitCode, Is.Zero, _first.Output);

    [Test]
    public void Then_it_reports_each_Outing_with_what_it_covered_and_its_gaps() =>
        Assert.That(_first.Output, Does.Match(@"Pavement and Park\s+2026-10-18\s+0\.33 km\s+4\d% covered\s+3 Segments\s+1 gap: 1[34]\d m"));

    [Test]
    public void Then_it_says_which_files_it_skipped_and_why() =>
        Assert.That(_first.Output, Does.Match(@"swim\.gpx\s+skipped: .*swimming.*not a run, walk, hike or ride"));

    [Test]
    public void Then_it_sums_up() =>
        Assert.That(_first.Output, Does.Contain("3 imported, 0 already there, 1 skipped"));

    [Test]
    public void Then_importing_again_changes_nothing() =>
        Assert.That(_again.Output, Does.Contain("0 imported, 3 already there, 1 skipped"));

    //  The user's tracks start at home: the report never shows a position.
    [Test]
    public void Then_it_never_prints_a_position() =>
        Assert.That(_first.Output, Does.Not.Contain("51.55").And.Not.Contain("-0.16"));

    [Test]
    public async Task Then_a_missing_folder_fails_and_says_so()
    {
        var (exitCode, output) = await Ct("outings", "import-gpx", "no-such-folder", "--database", TestDatabase.ConnectionString);
        Assert.That((exitCode, output.Contains("no-such-folder", StringComparison.Ordinal)), Is.EqualTo((1, true)));
    }
}
