using WendYourWay.Cli;
using WendYourWay.Data;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend outings score [--all]` (PLAN T097, STORY-045): the Outings not scored yet scored, or every
/// one again; and the rebuild, which leaves them to be scored, scoring them after it commits. On
/// Fixtures/same-ground.osm with the invented Run of Fixtures/pavement-and-park.gpx.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ScoringOutingsFromTheCommandLine
{
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
        var import = await Ct("outings", "import-gpx", Fixtures.Path("pavement-and-park.gpx"), "--database", TestDatabase.ConnectionString);
        Assert.That(import.ExitCode, Is.Zero, import.Output);
    }

    private static async Task Forget()
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var command = dataSource.CreateCommand("update app.outings set score = null, matched_share = null");
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task Imported_Outings_are_scored_already()
    {
        var (exitCode, output) = await Ct("outings", "score", "--database", TestDatabase.ConnectionString);
        Assert.That(exitCode, Is.Zero, output);
        Assert.That(output, Does.Match(@"Outings scored\s+0\b"));
    }

    [Test]
    public async Task Those_not_scored_are_scored()
    {
        await Forget();
        var (exitCode, output) = await Ct("outings", "score", "--database", TestDatabase.ConnectionString);
        Assert.That(exitCode, Is.Zero, output);
        Assert.That(output, Does.Match(@"Outings scored\s+1\b").And.Match(@"can't be rated\s+\d+"));
    }

    [Test]
    public async Task All_scores_every_Outing_again()
    {
        var (exitCode, output) = await Ct("outings", "score", "--all", "--database", TestDatabase.ConnectionString);
        Assert.That(exitCode, Is.Zero, output);
        Assert.That(output, Does.Match(@"Outings scored\s+1\b"));
    }

    //  T115: a Saved Route saved before the stars (no score, no Activity), along the imported Run's line.
    [Test]
    public async Task Saved_Routes_saved_before_the_stars_are_scored_too()
    {
        await using (var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString))
        await using (var command = dataSource.CreateCommand("""
            delete from app.saved_routes;
            insert into app.saved_routes (name, length_m, for_map, traversals)
            select 'Old loop', length_m,
                   jsonb_build_object('line', (select jsonb_agg(jsonb_build_object('latitude', ST_Y(d.geom), 'longitude', ST_X(d.geom)) order by d.path)
                                                 from ST_DumpPoints(ST_Transform(geom, 4326)) d)),
                   '[]'::jsonb
              from app.outings limit 1
            """))
            await command.ExecuteNonQueryAsync();
        var (exitCode, output) = await Ct("outings", "score", "--database", TestDatabase.ConnectionString);
        Assert.That(exitCode, Is.Zero, output);
        Assert.That(output, Does.Match(@"Saved Routes scored\s+1\b"));
    }

    [Test]
    public async Task Stopped_it_keeps_what_it_scored_and_says_so()
    {
        await Forget();
        using var output = new StringWriter();
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();
        var exitCode = await CommandLine.Run(["outings", "score", "--database", TestDatabase.ConnectionString], output, stopped.Token);
        Assert.That((exitCode, output.ToString().Contains("wend outings score", StringComparison.Ordinal)), Is.EqualTo((1, true)), output.ToString());
    }

    //  The rebuild clears the scores in its transaction and scores them in a phase after it commits.
    [Test]
    public async Task A_rebuild_scores_the_Outings_after_it_commits()
    {
        var (exitCode, output) = await Ct("network", "rebuild", "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"));
        Assert.That(exitCode, Is.Zero, output);
        Assert.That(output, Does.Match(@"(?m)^\s*outings scored\b.*\d+(\.\d+)? s\s*$").And.Match(@"Outings scored\s+1\b"));
    }
}
