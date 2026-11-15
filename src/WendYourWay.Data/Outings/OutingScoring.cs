using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Data.Outings;

/// <summary>What a pass of scoring did.</summary>
/// <param name="Scored">Outings given a score in this pass, including those that can't be rated.</param>
/// <param name="Unrated">Of those, how many can't be rated: matched for too little of their length, or not at all.</param>
public sealed record OutingScores(int Scored, int Unrated);

/// <summary>
/// The app's score for each Outing (PLAN T097, STORY-045): its track matched onto the Network as an
/// imported route is, and scored as a Loop is, with the Default weights for its Activity, as the
/// stars' cut points were. Kept in app.outings, each Outing on its own, so a pass can be stopped
/// and run again, and one that can't be matched doesn't stop the rest.
/// </summary>
public static class OutingScoring
{
    private const double MarginM = 200;
    private const double FurthestM = 45_000;


    /// <summary>
    /// Scores the Outings not scored yet, or, with <paramref name="all"/>, every one again (after a
    /// scoring rule changes); only those in <paramref name="ids"/> when given. Reports each Outing
    /// scored to <paramref name="progress"/>, with how many there are.
    /// </summary>
    public static async Task<OutingScores> ScoreAsync(
        NpgsqlDataSource dataSource, bool all = false, IReadOnlyList<int>? ids = null, Action<int, int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var outings = new List<(int Id, Activity Activity, LatLon[] Track)>();
        await using (var query = dataSource.CreateCommand("""
            select id, activity::text,
                   array(select ST_Y(d.geom) from ST_DumpPoints(ST_Transform(geom, 4326)) d order by d.path),
                   array(select ST_X(d.geom) from ST_DumpPoints(ST_Transform(geom, 4326)) d order by d.path)
              from app.outings
             where (@all or matched_share is null) and (@ids::int[] is null or id = any(@ids))
             order by id
            """))
        {
            query.Parameters.Add(new NpgsqlParameter("all", all));
            query.Parameters.Add(new NpgsqlParameter("ids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer) { Value = (object?)ids?.ToArray() ?? DBNull.Value });
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var (lats, lons) = (reader.GetFieldValue<double[]>(2), reader.GetFieldValue<double[]>(3));
                outings.Add((reader.GetInt32(0), ActivityColumn.OfOuting(reader.IsDBNull(1) ? null : reader.GetString(1)),
                    [.. lats.Zip(lons, (lat, lon) => new LatLon(lat, lon))]));
            }
        }

        var matching = new RouteMatching(dataSource);
        var (scored, unrated) = (0, 0);
        foreach (var (id, activity, track) in outings)
        {
            var (score, share, shape) = await ScoreOneAsync(matching, activity, track, cancellationToken);
            await using var update = dataSource.CreateCommand(
                "update app.outings set score = @score, matched_share = @share, off_road_share = @offRoad, climb_per_km = @climb where id = @id");
            update.Parameters.Add(new NpgsqlParameter("score", (float)score));
            update.Parameters.Add(new NpgsqlParameter("share", (float)share));
            update.Parameters.Add(new NpgsqlParameter("offRoad", NpgsqlTypes.NpgsqlDbType.Real) { Value = (object?)(float?)shape?.OffRoadShare ?? DBNull.Value });
            update.Parameters.Add(new NpgsqlParameter("climb", NpgsqlTypes.NpgsqlDbType.Real) { Value = (object?)(float?)shape?.ClimbPerKm ?? DBNull.Value });
            update.Parameters.Add(new NpgsqlParameter("id", id));
            await update.ExecuteNonQueryAsync(cancellationToken);
            scored++;
            if (Stars.Of(activity, score, share, shape?.OffRoadShare, shape?.ClimbPerKm) is null) unrated++;
            progress?.Invoke(scored, outings.Count);
        }
        return new OutingScores(scored, unrated);
    }

    /// <summary>
    /// Scores each Saved Route with no score (T115: saved before the stars, T097) as an Outing is,
    /// its line matched onto the Network; one with no Activity kept is scored, and kept, as a Run.
    /// Returns how many were scored.
    /// </summary>
    public static async Task<int> ScoreSavedRoutesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var routes = new List<(int Id, Activity Activity, LatLon[] Line)>();
        await using (var query = dataSource.CreateCommand("select id, activity::text as activity, for_map::text as for_map from app.saved_routes where score is null order by id"))
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var loop = System.Text.Json.JsonSerializer.Deserialize<Planning.PlannedLoop>(reader.Get<string>("for_map"), AppJson.Options)!;
                var activity = ActivityColumn.OfSavedRoute(reader.MaybeText("activity"));
                routes.Add((reader.Get<int>("id"), activity, [.. loop.Line]));
            }
        }

        var matching = new RouteMatching(dataSource);
        foreach (var (id, activity, line) in routes)
        {
            var (score, share, shape) = await ScoreOneAsync(matching, activity, line, cancellationToken);
            await using var update = dataSource.CreateCommand("""
                update app.saved_routes
                   set activity = @activity::activity, score = @score, matched_share = @share, off_road_share = @offRoad, climb_per_km = @climb
                 where id = @id
                """);
            update.Parameters.Add(new NpgsqlParameter("activity", ActivityColumn.Of(activity)));
            update.Parameters.Add(new NpgsqlParameter("score", (float)score));
            update.Parameters.Add(new NpgsqlParameter("share", (float)share));
            update.Parameters.Add(new NpgsqlParameter("offRoad", NpgsqlTypes.NpgsqlDbType.Real) { Value = (object?)(float?)shape?.OffRoadShare ?? DBNull.Value });
            update.Parameters.Add(new NpgsqlParameter("climb", NpgsqlTypes.NpgsqlDbType.Real) { Value = (object?)(float?)shape?.ClimbPerKm ?? DBNull.Value });
            update.Parameters.Add(new NpgsqlParameter("id", id));
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        return routes.Count;
    }

    // Its score, the share of its length matched, and what was matched as the planner sees it (its
    // share off-road and climbing, T104); a track that can't be matched at all scores nothing,
    // matched for none of it.
    private static async Task<(double Score, double Share, Breakdown? Shape)> ScoreOneAsync(RouteMatching matching, Activity activity, LatLon[] track, CancellationToken cancellationToken)
    {
        if (track.Length < 2) return (double.NaN, 0, null);
        try
        {
            var matched = await matching.MatchAsync(track, activity, MarginM, track[0].MetresTo(track[^1]) <= Planning.PlannedLoop.ClosedWithinM, FurthestM, cancellationToken);
            if (matched.Match.LengthM <= 0 || matched.RouteLengthM <= 0) return (double.NaN, 0, null);
            return (LoopScorer.Score(matched.Slice, matched.Match.Loop, ScoringWeights.Default.For(activity)), matched.Match.LengthM / matched.RouteLengthM,
                Breakdown.Of(matched.Slice, matched.Match.Loop));
        }
        catch (Exception e) when (e is not OperationCanceledException and not NpgsqlException)
        {
            return (double.NaN, 0, null);
        }
    }
}
