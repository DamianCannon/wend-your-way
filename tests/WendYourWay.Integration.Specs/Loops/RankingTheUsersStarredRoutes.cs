using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// The rated-Loop guard for the user's own stars (PLAN T097, STORY-045 AC4; user: "Starred routes in
/// London, 2+ apart"): every Outing and Saved Route the user has starred, in the app's own database,
/// that lies on London's Network, scored live under the rules as they are. It fails if the app scores
/// one above another of the same Activity the user gave at least 2 more stars. Prints counts and
/// Outing or Saved Route numbers only, never names or places. Read-only.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
//  Run only when asked, by name (T115; user, on making it a report: "idk do I really care?"): it reads
//  the user's live ratings, so new ones would otherwise fail the suite until it's re-based.
[Explicit("Reads the user's live ratings: run it by name to see where the app and the user part.")]
public class RankingTheUsersStarredRoutes
{
    /// <summary>A starred route as the guard scored it.</summary>
    internal sealed record Starred(string Label, Activity Activity, int UserStars, double Score, double Ranked = 0);

    /// <summary>
    /// The pairs the app ranks against the user: of one Activity, the user's stars at least 2 apart,
    /// the one they gave fewer scoring higher.
    /// </summary>
    internal static IReadOnlyList<(Starred Better, Starred Worse)> Disagreements(IReadOnlyList<Starred> starred) =>
        [.. from better in starred
            from worse in starred
            where better.Activity == worse.Activity && better.UserStars - worse.UserStars >= Stars.LeastApart && worse.Score > better.Score
            select (better, worse)];

    private readonly List<Starred> _starred = [];
    private int _read;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        await using var app = await RealNetwork.OpenOrIgnore(RealNetwork.GreatBritain);
        await using var london = await RealNetwork.OpenOrIgnore(RealNetwork.London);
        var routes = new List<(string Label, Activity Activity, int Stars, LatLon[] Line)>();
        await using (var stars = app.CreateCommand("select count(*) from information_schema.columns where table_schema = 'app' and table_name = 'saved_routes' and column_name = 'activity'"))
        {
            if ((long)(await stars.ExecuteScalarAsync())! == 0)
                Assert.Ignore("The app's database is from before the stars (T097): run the app or a wend command once to bring it up to date.");
        }
        await using (var outings = app.CreateCommand("""
            select id, activity::text, user_stars,
                   array(select ST_Y(d.geom) from ST_DumpPoints(ST_Transform(geom, 4326)) d order by d.path),
                   array(select ST_X(d.geom) from ST_DumpPoints(ST_Transform(geom, 4326)) d order by d.path)
              from app.outings where user_stars is not null order by id
            """))
        await using (var reader = await outings.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var (lats, lons) = (reader.GetFieldValue<double[]>(3), reader.GetFieldValue<double[]>(4));
                routes.Add(($"Outing {reader.GetInt32(0)}", ActivityColumn.OfOuting(reader.IsDBNull(1) ? null : reader.GetString(1)), reader.GetInt16(2),
                    [.. lats.Zip(lons, (lat, lon) => new LatLon(lat, lon))]));
            }
        }
        await using (var saved = app.CreateCommand("""
            select id, activity::text, user_stars, for_map::text from app.saved_routes where user_stars is not null and activity is not null order by id
            """))
        await using (var reader = await saved.ExecuteReaderAsync())
        {
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            while (await reader.ReadAsync())
            {
                var loop = System.Text.Json.JsonSerializer.Deserialize<PlannedLoop>(reader.GetString(3), json)!;
                routes.Add(($"Saved Route {reader.GetInt32(0)}", reader.GetString(1) == "run" ? Activity.Run : Activity.GravelRide, reader.GetInt16(2), [.. loop.Line]));
            }
        }
        _read = routes.Count;
        if (_read == 0) Assert.Ignore("No starred routes yet: star some Outings or Saved Routes in the app.");

        var matching = new RouteMatching(london);
        foreach (var (label, activity, stars, line) in routes)
        {
            if (line.Length < 2) continue;
            MatchedRoute matched;
            try
            {
                matched = await matching.MatchAsync(line, activity, 200, Metres(line[0], line[^1]) <= 200, 45_000);
            }
            catch (Exception e) when (e is RouteNotMatchedException or InvalidOperationException or ArgumentException)
            {
                continue;
            }
            // Off London's Network, mostly: not one for this guard.
            if (matched.RouteLengthM <= 0 || matched.Match.LengthM / matched.RouteLengthM < Stars.LeastMatchedShare) continue;
            _starred.Add(new Starred(label, activity, stars, LoopScorer.Score(matched.Slice, matched.Match.Loop, ScoringWeights.Default.For(activity)),
                LoopScorer.Score(matched.Slice, matched.Match.Loop, ScoringWeights.Default.Ranking(activity))));
        }
        await TestContext.Out.WriteLineAsync($"STARRED {_read} starred, {_starred.Count} on London's Network, {_starred.Count(a => _starred.Any(b => a.Activity == b.Activity && a.UserStars - b.UserStars >= Stars.LeastApart))} with one 2 stars below them");
    }

    //  Judged as Loops are ranked (T105), a count that can only fall (user, 2026-11-10: "Count, by
    //  ranking"): 47 pairs when the user had rated the Runs of 2026 (115 by the score alone);
    //  re-based to 173 when the user had rated 2018, 2024 and 2025 too (576 by the score alone;
    //  user, 2026-11-11: "Re-base to 173"). A rule that ranks more of the user's routes against
    //  their stars fails it; one that ranks fewer lowers this.
    private const int KnownAgainst = 173;

    [Test]
    public void No_more_routes_are_ranked_above_one_the_user_gave_2_more_stars_than_known()
    {
        if (_starred.Count < 2) Assert.Ignore($"{_starred.Count} starred route on London's Network: nothing to rank.");
        var against = Disagreements([.. _starred.Select(s => s with { Score = s.Ranked })]);
        TestContext.Out.WriteLine($"STARRED pairs against the user: {against.Count} as Loops are ranked (T105; known {KnownAgainst}), {Disagreements(_starred).Count} by the score alone");
        Assert.That(against, Has.Count.LessThanOrEqualTo(KnownAgainst), string.Join("; ", against.Take(10).Select(p => FormattableString.Invariant(
            $"{p.Worse.Label} (★{p.Worse.UserStars}) ranks {p.Worse.Score:0.000}, above {p.Better.Label} (★{p.Better.UserStars}) at {p.Better.Score:0.000}"))));
    }

    private static double Metres(LatLon a, LatLon b)
    {
        const double MetresPerDegree = 111_320;
        var north = (a.Latitude - b.Latitude) * MetresPerDegree;
        var east = (a.Longitude - b.Longitude) * MetresPerDegree * Math.Cos((a.Latitude + b.Latitude) / 2 * Math.PI / 180);
        return Math.Sqrt(north * north + east * east);
    }
}

/// <summary>The starred routes' guard's rule (T097), on routes made up here.</summary>
public class FindingWhereTheAppRanksAgainstTheUser
{
    private static RankingTheUsersStarredRoutes.Starred Run(string label, int stars, double score) => new(label, Activity.Run, stars, score);

    [Test]
    public void A_route_scoring_above_one_the_user_gave_2_more_stars_is_against_them() =>
        Assert.That(RankingTheUsersStarredRoutes.Disagreements([Run("good", 5, 0.2), Run("poor", 3, 0.4)]).Select(p => (p.Better.Label, p.Worse.Label)),
            Is.EqualTo(new[] { ("good", "poor") }));

    [Test]
    public void One_star_apart_is_left_as_noise() =>
        Assert.That(RankingTheUsersStarredRoutes.Disagreements([Run("good", 4, 0.2), Run("poor", 3, 0.4)]), Is.Empty);

    [Test]
    public void Scored_in_the_user_s_order_is_no_disagreement() =>
        Assert.That(RankingTheUsersStarredRoutes.Disagreements([Run("good", 5, 0.4), Run("poor", 2, 0.2)]), Is.Empty);

    [Test]
    public void A_ride_and_a_run_are_never_ranked_against_each_other() =>
        Assert.That(RankingTheUsersStarredRoutes.Disagreements([Run("run", 5, 0.2), new("ride", Activity.GravelRide, 1, 0.4)]), Is.Empty);
}
