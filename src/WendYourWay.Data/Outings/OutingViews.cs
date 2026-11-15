using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Data.Outings;

/// <summary>An Outing as the Outings page lists it.</summary>
/// <param name="Coverage">The share of its track beside Segments it covered.</param>
/// <param name="Gaps">How many stretches left the Network.</param>
public sealed record OutingSummary(int Id, string Name, DateTimeOffset? StartedAt, double LengthM, double Coverage, int Gaps)
{
    /// <summary>The app's stars for it, one to five (T097), or none: not scored yet, or it can't be rated.</summary>
    public int? Stars { get; init; }

    /// <summary>Whether the app has scored it (T097): an Outing not scored yet waits for `wend outings score`.</summary>
    public bool Scored { get; init; }

    /// <summary>The user's own stars for it, one to five (T097), or none.</summary>
    public int? UserStars { get; init; }
}

/// <summary>A Segment an Outing covered, as a line on the map with its ground.</summary>
public sealed record CoveredSegment(IReadOnlyList<LatLon> Line, GroundKind Ground);

/// <summary>A stretch where an Outing left the Network (CONTEXT.md: Gap).</summary>
public sealed record OutingGap(IReadOnlyList<LatLon> Line, double LengthM);

/// <summary>
/// One Outing drawn on the map: its GPS track, the Segments it covered, and its gaps in track
/// order (STORY-030 AC2, AC3).
/// </summary>
public sealed record OutingView(int Id, string Name, IReadOnlyList<LatLon> Track, IReadOnlyList<CoveredSegment> Covered, IReadOnlyList<OutingGap> Gaps)
{
    /// <summary>
    /// What a route made from it is matched for (T084): a Gravel Ride if it was ridden (a road ride
    /// too, which has no Activity of its own), otherwise a Run.
    /// </summary>
    public WendYourWay.Core.Loops.Activity Activity { get; init; } = WendYourWay.Core.Loops.Activity.Run;

    /// <summary>
    /// Every point it recorded, what a route made from it is matched on (T084): <see cref="Track"/>
    /// is thinned for the map, and matched thinned, a route could take a junction on a road above a
    /// path's underpass for the path's (user, 2026-11-09). Not sent to the map.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<LatLon> RouteTrack { get; init; } = [];
}

/// <summary>The user's Outings, for the Outings page.</summary>
public interface IOutingViews
{
    /// <summary>Every Outing, latest first.</summary>
    Task<IReadOnlyList<OutingSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One Outing to draw, or null if there's no such Outing.</summary>
    Task<OutingView?> ViewAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives it the user's own stars, one to five (T097, STORY-045), or takes them away (null).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than one star, or more than five.</exception>
    Task RateAsync(int id, int? stars, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes it (T106; user: "Gone, never re-imported"): its track, coverage and gaps, so it no
    /// longer counts as ground covered; only its Strava id, its track's fingerprint and its start
    /// are kept, so no later import brings it back. Nothing happens for one not here.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames it (T111): the name trimmed; it stays through a re-import, which skips an Outing
    /// already here. Nothing happens for one not here.
    /// </summary>
    /// <exception cref="ArgumentException">A blank name.</exception>
    Task RenameAsync(int id, string name, CancellationToken cancellationToken = default);
}

/// <summary>Reads Outings from app.outings, with their coverage and gaps, in WGS84 for the map.</summary>
public sealed class PostgisOutingViews(NpgsqlDataSource dataSource) : IOutingViews
{
    // A recording has a point a second; on the map, a point every couple of metres is plenty.
    private const double TrackDetailM = 2;

    public async Task<IReadOnlyList<OutingSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        // The stars' cuts as last calibrated (T114).
        var cuts = await StarCalibration.KeptAsync(dataSource, cancellationToken);
        await using var query = dataSource.CreateCommand("""
            select o.id, o.name, o.started_at, o.length_m, coalesce(o.coverage, 0) as coverage,
                   (select count(*) from app.outing_gaps g where g.outing_id = o.id) as gaps,
                   o.activity::text as activity, o.score, o.matched_share, o.user_stars, o.off_road_share, o.climb_per_km
              from app.outings o
             order by o.started_at desc nulls last, o.id desc
            """);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        var outings = new List<OutingSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var (score, matchedShare) = (reader.Maybe<float>("score"), reader.Maybe<float>("matched_share"));
            outings.Add(new OutingSummary(
                reader.Get<int>("id"), reader.Get<string>("name"), reader.Maybe<DateTimeOffset>("started_at"),
                reader.Get<float>("length_m"), reader.Get<float>("coverage"), (int)reader.Get<long>("gaps"))
            {
                // T108: a Run's stars from its ranking value, which needs its shape.
                Stars = score is null || matchedShare is null ? null
                    : WendYourWay.Core.Scoring.Stars.Of(ActivityColumn.OfOuting(reader.MaybeText("activity")), score.Value, matchedShare.Value,
                        reader.Maybe<float>("off_road_share"), reader.Maybe<float>("climb_per_km"), cuts),
                UserStars = reader.Maybe<short>("user_stars"),
                Scored = matchedShare is not null,
            });
        }
        return outings;
    }

    public async Task RateAsync(int id, int? stars, CancellationToken cancellationToken = default)
    {
        UserStars.Check(stars);
        await using var command = dataSource.CreateCommand("update app.outings set user_stars = @stars where id = @id");
        command.Parameters.Add(new NpgsqlParameter("stars", NpgsqlTypes.NpgsqlDbType.Smallint) { Value = (object?)(short?)stars ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RenameAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An Outing needs a name.", nameof(name));
        await using var command = dataSource.CreateCommand("update app.outings set name = @name where id = @id");
        command.Parameters.Add(new NpgsqlParameter("name", name.Trim()));
        command.Parameters.Add(new NpgsqlParameter("id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        // One statement, so remembering it and deleting it can't come apart.
        await using var command = dataSource.CreateCommand("""
            with gone as (delete from app.outings where id = @id returning strava_id, track_hash, started_at)
            insert into app.deleted_outings (strava_id, track_hash, started_at) select strava_id, track_hash, started_at from gone
            """);
        command.Parameters.Add(new NpgsqlParameter("id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OutingView?> ViewAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var batch = dataSource.CreateBatch();
        batch.BatchCommands.Add(Command("""
            select o.name, array_agg(ST_Y(p.geom) order by p.path[1]), array_agg(ST_X(p.geom) order by p.path[1]), o.activity::text
              from app.outings o
             cross join lateral ST_DumpPoints(ST_Transform(ST_Simplify(o.geom, @detail), 4326)) p
             where o.id = @id
             group by o.id, o.name, o.activity
            """));
        batch.BatchCommands.Add(Command("""
            select o.name, array_agg(ST_Y(p.geom) order by p.path[1]), array_agg(ST_X(p.geom) order by p.path[1])
              from app.outings o
             cross join lateral ST_DumpPoints(ST_Transform(o.geom, 4326)) p
             where o.id = @id
             group by o.id, o.name
            """));
        batch.BatchCommands.Add(Command("""
            select s.ground::text, array_agg(ST_Y(p.geom) order by p.path[1]), array_agg(ST_X(p.geom) order by p.path[1])
              from app.outing_coverage c
              join network.segments s
                on s.osm_way_id = c.osm_way_id and s.from_node_id = c.from_node_id and s.to_node_id = c.to_node_id
             cross join lateral ST_DumpPoints(ST_Transform(s.geom, 4326)) p
             where c.outing_id = @id
             group by s.id, s.ground
             order by s.id
            """));
        batch.BatchCommands.Add(Command("""
            select g.length_m, array_agg(ST_Y(p.geom) order by p.path[1]), array_agg(ST_X(p.geom) order by p.path[1])
              from app.outing_gaps g
             cross join lateral ST_DumpPoints(ST_Transform(g.geom, 4326)) p
             where g.outing_id = @id
             group by g.id, g.length_m
             order by g.id
            """));

        await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var name = reader.GetString(0);
        var track = Line(reader);
        // A ride of any sort was on a bike, so a Gravel Ride's access; on foot, a Run's (T121: by its
        // Activity, as it's scored, where this read its kind to the same end).
        var activity = ActivityColumn.OfOuting(reader.IsDBNull(3) ? null : reader.GetString(3));

        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var recorded = Line(reader);

        await reader.NextResultAsync(cancellationToken);
        var covered = new List<CoveredSegment>();
        while (await reader.ReadAsync(cancellationToken)) covered.Add(new CoveredSegment(Line(reader), GroundOf(reader.GetString(0))));

        await reader.NextResultAsync(cancellationToken);
        var gaps = new List<OutingGap>();
        while (await reader.ReadAsync(cancellationToken)) gaps.Add(new OutingGap(Line(reader), reader.GetFloat(0)));

        return new OutingView(id, name, track, covered, gaps) { Activity = activity, RouteTrack = recorded };

        NpgsqlBatchCommand Command(string sql) => new(sql)
        {
            Parameters = { new NpgsqlParameter("id", id), new NpgsqlParameter("detail", TrackDetailM) },
        };
    }

    private static LatLon[] Line(NpgsqlDataReader reader)
    {
        var latitudes = reader.GetFieldValue<double[]>(1);
        var longitudes = reader.GetFieldValue<double[]>(2);
        return [.. latitudes.Select((latitude, i) => new LatLon(latitude, longitudes[i]))];
    }

    private static GroundKind GroundOf(string ground) => ground switch
    {
        "off_road" => GroundKind.OffRoad,
        "traffic_free" => GroundKind.TrafficFree,
        "connector" => GroundKind.Connector,
        _ => throw new InvalidOperationException($"Unknown ground '{ground}' in network.segments."),
    };
}
