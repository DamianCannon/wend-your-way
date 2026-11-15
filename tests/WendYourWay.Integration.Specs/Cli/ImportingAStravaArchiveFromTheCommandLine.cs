using WendYourWay.Cli;
using WendYourWay.Integration.Specs.Support;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>`wend outings import-strava &lt;export.zip&gt;` (PLAN T029).</summary>
[TestFixture]
[NonParallelizable]
public class ImportingAStravaArchiveFromTheCommandLine
{
    private (int ExitCode, string Output) _run;

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
        _run = await Ct("outings", "import-strava", SampleStravaArchive.Build(), "--database", TestDatabase.ConnectionString);
    }

    [Test]
    public void Then_it_succeeds() => Assert.That(_run.ExitCode, Is.Zero, _run.Output);

    [Test]
    public void Then_it_says_what_it_imported_by_kind() =>
        Assert.That(_run.Output, Does.Match(@"imported\s+5\s+\(run 3, walk 1, ride 1\)"));

    [Test]
    public void Then_it_says_what_it_skipped_and_why() =>
        Assert.That(_run.Output, Does.Match(@"skipped\s+2\s+no way on the ground: Swim 1, Virtual Ride 1"));

    //  An import of a whole archive takes minutes to hours: each step says how long it's been,
    //  so a slow one shows where the time went (T030's took 8.5 hours, with no way to tell).
    [Test]
    public void Then_its_progress_says_how_long_it_has_taken() =>
        Assert.That(_run.Output, Does.Match(@"read \d+ of \d+ +\d+:\d\d:\d\d").And.Match(@"covered \d+ of \d+ Outings +\d+:\d\d:\d\d"));

    //  same-ground.osm holds no country boundaries, so nothing is outside (T030).
    [Test]
    public void Then_it_says_how_many_never_entered_England_or_Wales() =>
        Assert.That(_run.Output, Does.Match(@"outside E&W\s+0\s+never in England or Wales"));

    //  The user's tracks start at home: the report never shows a position.
    [Test]
    public void Then_it_never_prints_a_position() =>
        Assert.That(_run.Output, Does.Not.Contain("51.55").And.Not.Contain("-0.16"));

    [Test]
    public async Task Then_a_file_that_isn_t_an_archive_fails_and_says_so()
    {
        var notAnArchive = Path.Combine(Directory.CreateTempSubdirectory("cutthrough-not-strava-").FullName, "export.zip");
        System.IO.Compression.ZipFile.Open(notAnArchive, System.IO.Compression.ZipArchiveMode.Create).Dispose();
        var (exitCode, output) = await Ct("outings", "import-strava", notAnArchive, "--database", TestDatabase.ConnectionString);
        Assert.That((exitCode, output.Contains("no activities.csv", StringComparison.Ordinal)), Is.EqualTo((1, true)));
    }
}
