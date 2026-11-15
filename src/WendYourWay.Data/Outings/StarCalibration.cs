using System.Text.Json;
using System.Text.Json.Nodes;
using WendYourWay.Core.Scoring;
using Npgsql;
using NpgsqlTypes;

namespace WendYourWay.Data.Outings;

/// <summary>
/// How calibrating changed one Activity's cuts (T114): how many Outings it was cut from, the cuts
/// before and after, and of those Outings the user has rated, how many the app's stars match and
/// how many are 2 or more apart, before and after.
/// </summary>
/// <param name="Kept">Too few to cut from: the cuts it had are kept.</param>
public sealed record ActivityCalibration(
    int From, bool Kept, IReadOnlyList<double> Before, IReadOnlyList<double> After,
    int Rated, int SameBefore, int SameAfter, int ApartBefore, int ApartAfter);

/// <summary>A calibration's report: a Run's and a ride's.</summary>
public sealed record Calibration(ActivityCalibration Run, ActivityCalibration Ride);

/// <summary>
/// The stars' cut points kept with the user's data (PLAN T114, `wend outings calibrate`): worked out
/// again from the Outings as they are, by the rule <see cref="StarCuts.Calibrate"/>; kept in
/// app.settings.star_cuts; read by the Outings and Saved Routes lists.
/// </summary>
public static class StarCalibration
{
    /// <summary>The cuts kept, each Activity's if usable; else the ones in code.</summary>
    public static async Task<StarCuts> KeptAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var query = dataSource.CreateCommand("select star_cuts::text from app.settings");
        if (await query.ExecuteScalarAsync(cancellationToken) is not string json) return StarCuts.InCode;
        var kept = JsonNode.Parse(json);
        return StarCuts.OrInCode(Numbers(kept?["run"]), Numbers(kept?["ride"]));
    }

    // Four numbers, or none where it's anything else.
    private static double[]? Numbers(JsonNode? node)
    {
        if (node is not JsonArray array) return null;
        var numbers = new List<double>();
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue<double>(out var number)) return null;
            numbers.Add(number);
        }
        return [.. numbers];
    }

    // An Outing that counts: its Activity, score and shape, the value it's cut by, and the user's stars.
    private sealed record Counted(Core.Loops.Activity Activity, double Score, double? OffRoad, double? Climb, double Value, int? UserStars)
    {
        public bool IsRun => Activity == Core.Loops.Activity.Run;
        public int? StarsBy(StarCuts cuts) => Stars.Of(Activity, Score, matchedShare: 1, OffRoad, Climb, cuts);
    }

    /// <summary>
    /// Works the cuts out again from the Outings matched for <see cref="Stars.LeastMatchedShare"/> or
    /// more and scored (a Run with its shape known), keeps them, and says what changed.
    /// </summary>
    public static async Task<Calibration> CalibrateAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var before = await KeptAsync(dataSource, cancellationToken);
        var counted = new List<Counted>();
        await using (var query = dataSource.CreateCommand("""
            select activity::text as activity, score, off_road_share, climb_per_km, user_stars
              from app.outings
             where score is not null and score <> 'NaN' and matched_share >= @least
            """))
        {
            query.Parameters.Add(new NpgsqlParameter("least", (float)Stars.LeastMatchedShare));
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var activity = ActivityColumn.OfOuting(reader.MaybeText("activity"));
                var score = reader.Get<float>("score");
                var (offRoad, climb) = (reader.Maybe<float>("off_road_share"), reader.Maybe<float>("climb_per_km"));
                var isRun = activity == Core.Loops.Activity.Run;
                if (isRun && (offRoad is null || climb is null)) continue;
                counted.Add(new Counted(activity, score, offRoad, climb, isRun ? Stars.RankingOf(score, offRoad!.Value, climb!.Value) : score, reader.Maybe<short>("user_stars")));
            }
        }
        var runs = counted.Where(c => c.IsRun).ToList();
        var rides = counted.Where(c => !c.IsRun).ToList();
        var after = StarCuts.Calibrate([.. runs.Select(r => r.Value)], [.. rides.Select(r => r.Value)], before);

        await using (var keep = dataSource.CreateCommand("""
            insert into app.settings (star_cuts) values (@cuts)
            on conflict (id) do update set star_cuts = excluded.star_cuts
            """))
        {
            var json = new JsonObject
            {
                ["run"] = new JsonArray([.. after.Run.Select(c => (JsonNode)c)]),
                ["ride"] = new JsonArray([.. after.Ride.Select(c => (JsonNode)c)]),
                ["at"] = DateTimeOffset.UtcNow,
                ["runs"] = runs.Count,
                ["rides"] = rides.Count,
            };
            keep.Parameters.Add(new NpgsqlParameter("cuts", NpgsqlDbType.Jsonb) { Value = json.ToJsonString() });
            await keep.ExecuteNonQueryAsync(cancellationToken);
        }

        return new Calibration(
            Report(runs, before, after, before.Run, after.Run),
            Report(rides, before, after, before.Ride, after.Ride));
    }

    private static ActivityCalibration Report(List<Counted> counted, StarCuts before, StarCuts after, IReadOnlyList<double> cutsBefore, IReadOnlyList<double> cutsAfter)
    {
        var rated = counted.Where(c => c.UserStars is not null).ToList();
        int Same(StarCuts cuts) => rated.Count(c => c.StarsBy(cuts) == c.UserStars);
        int Apart(StarCuts cuts) => rated.Count(c => Stars.Disagree(c.UserStars, c.StarsBy(cuts)));
        return new ActivityCalibration(counted.Count, counted.Count < StarCuts.LeastToCalibrate, cutsBefore, cutsAfter,
            rated.Count, Same(before), Same(after), Apart(before), Apart(after));
    }
}
