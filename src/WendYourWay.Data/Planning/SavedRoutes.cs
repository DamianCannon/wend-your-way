using WendYourWay.Core.Loops;
using System.Text.Json;
using WendYourWay.Core.Network;
using Npgsql;
using NpgsqlTypes;

namespace WendYourWay.Data.Planning;

/// <summary>A Saved Route as listed (T080): its name, when it was saved, and its km.</summary>
public sealed record SavedRouteSummary(int Id, string Name, DateTimeOffset SavedAt, double LengthKm)
{
    /// <summary>The user's own stars for it, one to five (T097), or none.</summary>
    public int? UserStars { get; init; }

    /// <summary>The app's stars for it (T097), from the score it was saved with; none if it had none, or it can't be rated.</summary>
    public int? Stars { get; init; }

    /// <summary>Whether it was saved with the app's score (T097): one saved before the stars has none.</summary>
    public bool Scored { get; init; }

    /// <summary>
    /// Whether the map has changed under it since it was saved (T103): a Segment of it, by its OSM
    /// identity, no longer on the Network, after a rebuild. Its line is kept as it was.
    /// </summary>
    public bool MapChanged { get; init; }

    /// <summary>What Saved Routes says of one the map has changed under (T103).</summary>
    public const string MapChangedNote = "The map has changed under this route since it was saved.";
}

/// <summary>A Saved Route opened (T080): its name, and the Loop as the map draws it.</summary>
public sealed record SavedRoute(int Id, string Name, PlannedLoop Loop);

/// <summary>The user's Saved Routes (T080, STORY-015, R5.1).</summary>
public interface ISavedRoutes
{
    /// <summary>Keeps <paramref name="planned"/> by <paramref name="name"/>, with its Segments by OSM identity; its id.</summary>
    /// <exception cref="ArgumentException">An empty name.</exception>
    /// <param name="score">The app's score for it (T097), for its stars; none, and it has none.</param>
    Task<int> SaveAsync(string name, PlannedLoop planned, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, RouteScore? score = null, CancellationToken cancellationToken = default);

    /// <summary>Every Saved Route, newest first.</summary>
    Task<IReadOnlyList<SavedRouteSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The Saved Route, or null if there's none of that id.</summary>
    Task<SavedRoute?> OpenAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Its Segments by OSM identity, in travel order.</summary>
    Task<IReadOnlyList<(SegmentKey Key, bool Forward)>> TraversalsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves it onto another map (T136): its line, Segments and the app's score now these; its
    /// name, date and the user's stars kept.
    /// </summary>
    Task MoveAsync(int id, PlannedLoop planned, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, RouteScore? score, CancellationToken cancellationToken = default);

    Task RenameAsync(int id, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives it the user's own stars, one to five (T097, STORY-045), or takes them away (null).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than one star, or more than five.</exception>
    Task RateAsync(int id, int? stars, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>Saved Routes in app.saved_routes, what the map draws as jsonb.</summary>
public sealed class PostgisSavedRoutes(NpgsqlDataSource dataSource) : ISavedRoutes
{
    private static JsonSerializerOptions Json => AppJson.Options;

    // A Segment as kept: its OSM way, its two nodes, and whether it was travelled from the first.
    private sealed record KeptTraversal(long Way, long From, long To, bool Forward);

    public async Task<int> SaveAsync(string name, PlannedLoop planned, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, RouteScore? score = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(traversals);
        var named = Named(name);
        await using var command = dataSource.CreateCommand("""
            insert into app.saved_routes (name, length_m, for_map, traversals, score, matched_share, activity, off_road_share, climb_per_km)
            values (@name, @length, @map, @traversals, @score, @share, @activity::activity, @offRoad, @climb)
            returning id
            """);
        command.Parameters.Add(new NpgsqlParameter("name", named));
        AddRoute(command.Parameters, planned, traversals, score);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task MoveAsync(int id, PlannedLoop planned, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, RouteScore? score, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(traversals);
        await using var command = dataSource.CreateCommand("""
            update app.saved_routes
               set length_m = @length, for_map = @map, traversals = @traversals, score = @score, matched_share = @share,
                   activity = coalesce(@activity::activity, activity), off_road_share = @offRoad, climb_per_km = @climb
             where id = @id
            """);
        command.Parameters.Add(new NpgsqlParameter("id", id));
        AddRoute(command.Parameters, planned, traversals, score);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // What a route is kept as: its length, what the map draws, its Segments, and the app's score.
    private static void AddRoute(NpgsqlParameterCollection parameters, PlannedLoop planned, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, RouteScore? score)
    {
        parameters.Add(new NpgsqlParameter("length", (float)planned.Breakdown.LengthM));
        parameters.Add(new NpgsqlParameter("map", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(planned, Json) });
        parameters.Add(new NpgsqlParameter("traversals", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(traversals.Select(t => new KeptTraversal(t.Key.OsmWayId, t.Key.FromNodeId, t.Key.ToNodeId, t.Forward)), Json),
        });
        parameters.Add(new NpgsqlParameter("score", NpgsqlDbType.Real) { Value = (object?)(float?)score?.Score ?? DBNull.Value });
        parameters.Add(new NpgsqlParameter("share", NpgsqlDbType.Real) { Value = (object?)(float?)score?.MatchedShare ?? DBNull.Value });
        parameters.Add(new NpgsqlParameter("offRoad", NpgsqlDbType.Real) { Value = (object?)(float?)score?.OffRoadShare ?? DBNull.Value });
        parameters.Add(new NpgsqlParameter("climb", NpgsqlDbType.Real) { Value = (object?)(float?)score?.ClimbPerKm ?? DBNull.Value });
        parameters.Add(new NpgsqlParameter("activity", NpgsqlDbType.Text) { Value = (object?)(score is null ? null : ActivityColumn.Of(score.Activity)) ?? DBNull.Value });
    }

    public async Task<IReadOnlyList<SavedRouteSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        // The stars' cuts as last calibrated (T114).
        var cuts = await WendYourWay.Data.Outings.StarCalibration.KeptAsync(dataSource, cancellationToken);
        await using var query = dataSource.CreateCommand("""
            select id, name, saved_at, length_m, user_stars, activity::text as activity, score, matched_share,
                off_road_share, climb_per_km,
                jsonb_array_length(traversals) = 0 or exists (
                    select 1 from jsonb_to_recordset(traversals) as t(way bigint, "from" bigint, "to" bigint)
                    where not exists (select 1 from network.segments s where s.osm_way_id = t.way and s.from_node_id = t."from" and s.to_node_id = t."to")) as map_changed
            from app.saved_routes order by saved_at desc, id desc
            """);
        var routes = new List<SavedRouteSummary>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var (activity, score, matchedShare) = (reader.MaybeText("activity"), reader.Maybe<float>("score"), reader.Maybe<float>("matched_share"));
            routes.Add(new SavedRouteSummary(reader.Get<int>("id"), reader.Get<string>("name"), reader.Get<DateTimeOffset>("saved_at"), Math.Round(reader.Get<float>("length_m") / 1000.0, 3))
            {
                UserStars = reader.Maybe<short>("user_stars"),
                Scored = score is not null,
                MapChanged = reader.Get<bool>("map_changed"),
                Stars = activity is null || score is null || matchedShare is null ? null
                    : WendYourWay.Core.Scoring.Stars.Of(ActivityColumn.OfSavedRoute(activity), score.Value, matchedShare.Value,
                        reader.Maybe<float>("off_road_share"), reader.Maybe<float>("climb_per_km"), cuts),
            });
        }
        return routes;
    }

    public async Task<SavedRoute?> OpenAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand("select name, for_map::text from app.saved_routes where id = @id");
        query.Parameters.Add(new NpgsqlParameter("id", id));
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new SavedRoute(id, reader.GetString(0), JsonSerializer.Deserialize<PlannedLoop>(reader.GetString(1), Json)!);
    }

    public async Task<IReadOnlyList<(SegmentKey Key, bool Forward)>> TraversalsAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand("select traversals::text from app.saved_routes where id = @id");
        query.Parameters.Add(new NpgsqlParameter("id", id));
        if (await query.ExecuteScalarAsync(cancellationToken) is not string json) return [];
        return [.. JsonSerializer.Deserialize<KeptTraversal[]>(json, Json)!.Select(t => (new SegmentKey(t.Way, t.From, t.To), t.Forward))];
    }

    public async Task RenameAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("update app.saved_routes set name = @name where id = @id");
        command.Parameters.Add(new NpgsqlParameter("name", Named(name)));
        command.Parameters.Add(new NpgsqlParameter("id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RateAsync(int id, int? stars, CancellationToken cancellationToken = default)
    {
        UserStars.Check(stars);
        await using var command = dataSource.CreateCommand("update app.saved_routes set user_stars = @stars where id = @id");
        command.Parameters.Add(new NpgsqlParameter("stars", NpgsqlTypes.NpgsqlDbType.Smallint) { Value = (object?)(short?)stars ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("delete from app.saved_routes where id = @id");
        command.Parameters.Add(new NpgsqlParameter("id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Named(string name) =>
        name?.Trim() is { Length: > 0 and <= 100 } named ? named : throw new ArgumentException("A Saved Route needs a name of up to 100 characters.", nameof(name));
}
