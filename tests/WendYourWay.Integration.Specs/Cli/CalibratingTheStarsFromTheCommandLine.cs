using WendYourWay.Cli;
using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend outings calibrate` (PLAN T114): the stars' cuts worked out again and kept, said per Activity
/// in counts and cut points only. Made-up scored Outings on a short line, no Network needed.
/// </summary>
[TestFixture]
[NonParallelizable]
public class CalibratingTheStarsFromTheCommandLine
{
    private static async Task<(int ExitCode, string Output)> Ct(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(args, output);
        return (exitCode, output.ToString());
    }

    private (int ExitCode, string Output) _ran;

    //  30 Runs ranked 0.00 to 0.29, the first rated 1 and the last 5; 5 rides, too few.
    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        await using (var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString))
        await using (var command = dataSource.CreateCommand("""
            insert into app.outings (kind, activity, name, source_file, track_hash, geom, score, matched_share, off_road_share, climb_per_km, user_stars)
            select 'run'::outing_kind, 'run'::activity, 'Run ' || i, 't114-test', 'run-' || i,
                   ST_MakeLine(ST_MakePoint(500000, 180000 + i), ST_MakePoint(500100, 180000 + i))::geometry(LineString, 27700),
                   (i / 100.0)::real, 1::real, 0::real, 0::real, (case i when 0 then 1 when 29 then 5 end)::smallint
              from generate_series(0, 29) i
            union all
            select 'ride'::outing_kind, null::activity, 'Ride ' || i, 't114-test', 'ride-' || i,
                   ST_MakeLine(ST_MakePoint(600000, 180000 + i), ST_MakePoint(600100, 180000 + i))::geometry(LineString, 27700),
                   (i / 1000.0)::real, 1::real, null::real, null::real, null::smallint
              from generate_series(0, 4) i
            """))
            await command.ExecuteNonQueryAsync();
        _ran = await Ct("outings", "calibrate", "--database", TestDatabase.ConnectionString);
    }

    [Test]
    public void It_succeeds() => Assert.That(_ran.ExitCode, Is.Zero, _ran.Output);

    [Test]
    public void It_says_how_many_Runs_it_was_cut_from() => Assert.That(_ran.Output, Does.Contain("Runs: cut from 30 matched"));

    //  Of 30, the values at 3, 9, 15 and 21.
    [Test]
    public void It_says_a_Runs_cuts_before_and_after() =>
        Assert.That(_ran.Output, Does.Contain("before  0.1910  0.4008  0.5575  0.7172").And.Contain("after   0.0300  0.0900  0.1500  0.2100"));

    //  The Run ranked 0.29 the user gave 5: 2 stars before, 5 after; the one ranked 0 the user gave 1, 1 both.
    [Test]
    public void It_says_how_many_rated_Runs_match_before_and_after() =>
        Assert.That(_ran.Output, Does.Contain("of 2 you've rated: the same stars 1 → 2, 2 or more apart 1 → 0"));

    [Test]
    public void Too_few_rides_keep_their_cuts() => Assert.That(_ran.Output, Does.Contain("Rides: too few to cut from (5 matched, 20 needed): kept"));

    [Test]
    public void It_says_the_cuts_are_kept() => Assert.That(_ran.Output, Does.Contain("Kept: the stars are cut by these"));
}
