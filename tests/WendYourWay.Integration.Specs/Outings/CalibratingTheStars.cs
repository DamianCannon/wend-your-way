using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// `wend outings calibrate` (PLAN T114): the stars' cut points worked out again from the Outings kept,
/// kept in app.settings, and read by the lists. The Outings are made up: scores, shapes and stars
/// set by hand on a short line, no Network needed.
/// </summary>
[TestFixture]
[NonParallelizable]
public class CalibratingTheStars
{
    private NpgsqlDataSource _dataSource = null!;
    private int _made;

    [OneTimeSetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    //  40 Runs ranked 0.00 to 0.39 (no shape added: off-road 0, flat), 25 rides scoring 0.000 to
    //  0.024; and Outings that don't count: matched for half, scored NaN, a Run with no shape, not scored.
    [SetUp]
    public async Task Outings()
    {
        await Execute("delete from app.outings; update app.settings set star_cuts = null");
        for (var i = 0; i < 40; i++) await Make("run", "run", i / 100.0, matched: 1, offRoad: 0, climb: 0, stars: i switch { 0 => 1, 39 => 5, _ => null });
        for (var i = 0; i < 25; i++) await Make("ride", null, i / 1000.0, matched: 1, offRoad: null, climb: null, stars: null);
        await Make("run", "run", 9, matched: 0.5, offRoad: 0, climb: 0, stars: 5);
        await Make("run", "run", double.NaN, matched: 1, offRoad: 0, climb: 0, stars: null);
        await Make("run", "run", 9, matched: 1, offRoad: null, climb: null, stars: null);
        await Make("run", "run", null, matched: null, offRoad: null, climb: null, stars: 3);
    }

    private async Task Execute(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task Make(string kind, string? activity, double? score, double? matched, double? offRoad, double? climb, int? stars)
    {
        var n = ++_made;
        await using var command = _dataSource.CreateCommand($"""
            insert into app.outings (kind, activity, name, source_file, track_hash, geom, score, matched_share, off_road_share, climb_per_km, user_stars)
            values (@kind::outing_kind, @activity::activity, @name, 't114-test', @hash,
                    ST_MakeLine(ST_MakePoint(500000, 180000 + {n}), ST_MakePoint(500100, 180000 + {n}))::geometry(LineString, 27700),
                    @score, @matched, @offRoad, @climb, @stars)
            """);
        command.Parameters.Add(new NpgsqlParameter("kind", kind));
        command.Parameters.Add(new NpgsqlParameter("activity", (object?)activity ?? DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("name", $"Outing {n}"));
        command.Parameters.Add(new NpgsqlParameter("hash", $"t114-{n}"));
        command.Parameters.Add(new NpgsqlParameter("score", score is { } s ? (float)s : DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("matched", matched is { } m ? (float)m : DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("offRoad", offRoad is { } o ? (float)o : DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("climb", climb is { } c ? (float)c : DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("stars", stars is { } u ? (short)u : DBNull.Value));
        await command.ExecuteNonQueryAsync();
    }

    private static readonly double[] RunCuts = [0.04, 0.12, 0.20, 0.28];
    private static readonly double[] RideCuts = [0.005, 0.010, 0.015, 0.020];

    [Test]
    public async Task Until_calibrated_the_cuts_are_the_ones_in_code() =>
        Assert.That(await StarCalibration.KeptAsync(_dataSource), Is.SameAs(StarCuts.InCode).Or.EqualTo(StarCuts.InCode));

    [Test]
    public async Task A_Runs_cuts_are_worked_out_from_the_Runs_that_count()
    {
        await StarCalibration.CalibrateAsync(_dataSource);
        Assert.That((await StarCalibration.KeptAsync(_dataSource)).Run, Is.EqualTo(RunCuts).Within(1e-6));
    }

    [Test]
    public async Task A_rides_cuts_are_worked_out_from_the_rides_that_count()
    {
        await StarCalibration.CalibrateAsync(_dataSource);
        Assert.That((await StarCalibration.KeptAsync(_dataSource)).Ride, Is.EqualTo(RideCuts).Within(1e-6));
    }

    [Test]
    public async Task It_says_how_many_of_each_it_was_cut_from()
    {
        var calibrated = await StarCalibration.CalibrateAsync(_dataSource);
        Assert.That((calibrated.Run.From, calibrated.Ride.From), Is.EqualTo((40, 25)));
    }

    [Test]
    public async Task It_says_the_cuts_before_and_after()
    {
        var calibrated = await StarCalibration.CalibrateAsync(_dataSource);
        Assert.That((calibrated.Run.Before, calibrated.Run.After, calibrated.Ride.Before, calibrated.Ride.After),
            Is.EqualTo((StarCuts.InCode.Run, RunCuts, StarCuts.InCode.Ride, RideCuts)).Using<object>(Alike));
    }

    //  Of the Outings rated that count, the Run ranked 0.00 the user gave 1 (1 before and after) and
    //  the one ranked 0.39 they gave 5: 2 by the cuts in code, 5 by the new ones.
    [Test]
    public async Task It_says_how_many_rated_match_and_how_many_are_2_or_more_apart_before_and_after()
    {
        var run = (await StarCalibration.CalibrateAsync(_dataSource)).Run;
        Assert.That((run.Rated, run.SameBefore, run.SameAfter, run.ApartBefore, run.ApartAfter), Is.EqualTo((2, 1, 2, 1, 0)));
    }

    [Test]
    public async Task The_Outings_are_listed_with_stars_from_the_cuts_kept()
    {
        await StarCalibration.CalibrateAsync(_dataSource);
        var listed = (await new PostgisOutingViews(_dataSource).ListAsync()).Single(o => o.UserStars == 5 && o.Stars is not null);
        Assert.That(listed.Stars, Is.EqualTo(5));
    }

    //  19 rides left (0.000 to 0.018) after a calibration from 25: the rides' cuts stay.
    [Test]
    public async Task An_Activity_with_too_few_keeps_the_cuts_it_has()
    {
        await StarCalibration.CalibrateAsync(_dataSource);
        await Execute("delete from app.outings where kind = 'ride' and score >= 0.0185");
        var calibrated = await StarCalibration.CalibrateAsync(_dataSource);
        Assert.That((calibrated.Ride.From, calibrated.Ride.Kept, (await StarCalibration.KeptAsync(_dataSource)).Ride), Is.EqualTo((19, true, RideCuts)).Using<object>(Alike));
    }

    //  20 rides left (0.000 to 0.019): enough, cut at the 4th, 8th, 12th and 16th.
    [Test]
    public async Task Twenty_of_an_Activity_are_enough()
    {
        await Execute("delete from app.outings where kind = 'ride' and score >= 0.0195");
        var calibrated = await StarCalibration.CalibrateAsync(_dataSource);
        Assert.That((calibrated.Ride.From, calibrated.Ride.Kept, calibrated.Ride.After), Is.EqualTo((20, false, new[] { 0.004, 0.008, 0.012, 0.016 })).Using<object>(Alike));
    }

    [Test]
    public async Task Cuts_kept_that_cant_be_used_are_the_ones_in_code()
    {
        await Execute("""
            insert into app.settings (star_cuts) values ('{"run":[0.3,0.2,0.1,0.4],"ride":"nonsense"}')
            on conflict (id) do update set star_cuts = excluded.star_cuts
            """);
        Assert.That(await StarCalibration.KeptAsync(_dataSource), Is.EqualTo(StarCuts.InCode).Using<StarCuts>((a, b) => a.Run.SequenceEqual(b.Run) && a.Ride.SequenceEqual(b.Ride)));
    }

    //  Tuples of counts, flags and cut lists, the lists alike within a millionth.
    private static bool Alike(object a, object b) => (a, b) switch
    {
        (System.Runtime.CompilerServices.ITuple x, System.Runtime.CompilerServices.ITuple y) =>
            x.Length == y.Length && Enumerable.Range(0, x.Length).All(i => Alike(x[i]!, y[i]!)),
        (IEnumerable<double> x, IEnumerable<double> y) => x.Count() == y.Count() && x.Zip(y).All(p => Math.Abs(p.First - p.Second) < 1e-6),
        _ => Equals(a, b),
    };
}
