using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace WendYourWay.Data.Outings;

/// <summary>What importing one GPX file did, and what the Outing covered.</summary>
/// <param name="Name">The Outing's name, from the file.</param>
/// <param name="AlreadyImported">The same Outing was imported before, so nothing changed.</param>
/// <param name="SameStartOnly">Already here, known by its start (within a minute), not as the very same track (T130).</param>
/// <param name="StartedAt">When it started, if the file has timestamps.</param>
/// <param name="LengthM">The track's length.</param>
/// <param name="Coverage">The share of the track beside Segments it covered.</param>
/// <param name="SegmentsCovered">How many Segments it covered.</param>
/// <param name="GapsM">The lengths of the stretches where it left the Network, in track order.</param>
public sealed record OutingImport(
    string Name, bool AlreadyImported, DateTimeOffset? StartedAt, double LengthM, double Coverage, int SegmentsCovered, IReadOnlyList<double> GapsM)
{
    public bool SameStartOnly { get; init; }
}

/// <summary>Why a track offered wasn't imported (T130), so the reasons can be counted apart.</summary>
public enum OutingSkip
{
    /// <summary>Most of its points have no time: a route someone planned, not a run or ride recorded.</summary>
    PlannedRoute,

    /// <summary>It never enters England or Wales.</summary>
    OutsideEnglandAndWales,

    /// <summary>The user deleted this Outing (T106).</summary>
    Deleted,
}

/// <summary>
/// Why a track wasn't imported (T130): carried in the <see cref="InvalidDataException"/> that says so
/// (sealed, so not a type of its own), for the reasons to be counted apart.
/// </summary>
public static class OutingSkips
{
    private const string Key = "WendYourWay.OutingSkip";

    /// <summary>The exception a track refused for <paramref name="reason"/> is thrown as.</summary>
    public static InvalidDataException Refused(OutingSkip reason, string message)
    {
        var refused = new InvalidDataException(message);
        refused.Data[Key] = reason;
        return refused;
    }

    /// <summary>Why <paramref name="e"/> refused a track; none if it's of another kind (a file that can't be read).</summary>
    public static OutingSkip? Of(Exception e) => e?.Data[Key] as OutingSkip?;
}

/// <summary>What became of a recorded track offered as an Outing.</summary>
internal enum Adding
{
    /// <summary>A new Outing.</summary>
    Added,

    /// <summary>The same Outing was already here: the same Strava activity, or the very same track.</summary>
    AlreadyThere,

    /// <summary>An Outing starting within a minute of it was already here (T130: counted apart).</summary>
    SameStart,

    /// <summary>
    /// It never enters England or Wales (a holiday abroad, a hike in Scotland): the Network
    /// doesn't cover where it went, so it isn't an Outing (STORY-005 AC5).
    /// </summary>
    OutsideEnglandAndWales,

    /// <summary>The user deleted this Outing (T106): it's never imported again.</summary>
    Deleted,
}

/// <summary>Imports the user's own runs and rides into app.outings (PLAN T016, T029).</summary>
public static class OutingImporter
{
    // The same activity from two sources (a GPX downloaded from Strava, then the archive's
    // original file) starts within this of itself, though its points differ a little.
    private static readonly TimeSpan SameStart = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Imports one GPX, TCX or FIT file and works out what it covered, in one transaction.
    /// Importing the same Outing again changes nothing.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file isn't a track, is a planned route rather than a recording, or isn't a run, walk,
    /// hike or ride; the message says which, without naming the file.
    /// </exception>
    public static async Task<OutingImport> ImportGpxAsync(NpgsqlDataSource dataSource, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var track = RecordedTrack.Read(path);
        // Known as a planned route before what it was is asked: Wend Your Way's own exports say neither (T133).
        RefusePlanned(track);
        var kind = KindOf(track);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var (id, adding) = await AddAsync(connection, transaction, track, kind, track.Name, Path.GetFileName(path), stravaId: null, cancellationToken);
        if (adding == Adding.OutsideEnglandAndWales) throw OutingSkips.Refused(OutingSkip.OutsideEnglandAndWales, "it's outside England and Wales");
        if (adding == Adding.Deleted) throw OutingSkips.Refused(OutingSkip.Deleted, "you deleted it, so it isn't imported again");
        var added = adding == Adding.Added;
        if (added) await CoverAsync(connection, transaction, [id], cancellationToken);

        await using var report = new NpgsqlCommand("""
            select o.length_m, coalesce(o.coverage, 0),
                   (select count(*) from app.outing_coverage c where c.outing_id = o.id),
                   (select coalesce(array_agg(g.length_m::double precision order by g.id), '{}') from app.outing_gaps g where g.outing_id = o.id)
              from app.outings o where o.id = @id
            """, connection, transaction);
        report.Parameters.Add(new NpgsqlParameter("id", id));
        OutingImport result;
        await using (var reader = await report.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            result = new OutingImport(track.Name, AlreadyImported: !added, track.StartedAt,
                reader.GetFloat(0), reader.GetFloat(1), (int)reader.GetInt64(2), reader.GetFieldValue<double[]>(3)) { SameStartOnly = adding == Adding.SameStart };
        }
        await transaction.CommitAsync(cancellationToken);
        // Scored after it's kept: matching reads the Network outside this transaction (T097).
        if (added) await OutingScoring.ScoreAsync(dataSource, ids: [id], cancellationToken: cancellationToken);
        return result;
    }

    /// <summary>
    /// Adds a recorded track as an Outing of this kind, without covering it, unless it's already
    /// here: the same Strava activity, the same track, or an Outing starting within a minute of it
    /// (which then gains the Strava id, and the archive's kind). Returns the Outing's id and
    /// what became of the track; nothing is added for a track outside England and Wales.
    /// </summary>
    /// <exception cref="InvalidDataException">The track is a planned route, not a recording.</exception>
    internal static async Task<(int Id, Adding Adding)> AddAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, RecordedTrack track, string kind, string name, string file, long? stravaId,
        CancellationToken cancellationToken)
    {
        RefusePlanned(track);

        var hash = HashOf(track);
        // One the user deleted (T106), known again as an Outing already here would be: the same
        // Strava activity, the same track, or (with no Strava id to tell them apart) the same start.
        await using (var deleted = new NpgsqlCommand("""
            select exists (
              select 1 from app.deleted_outings
               where (@strava is not null and strava_id = @strava)
                  or track_hash = @hash
                  or ((strava_id is null or @strava is null)
                      and @started is not null and started_at between @started - @same and @started + @same))
            """, connection, transaction))
        {
            AddCommon(deleted.Parameters);
            deleted.Parameters.Add(new NpgsqlParameter("same", SameStart));
            if ((bool)(await deleted.ExecuteScalarAsync(cancellationToken))!) return (0, Adding.Deleted);
        }

        await using (var existing = new NpgsqlCommand("""
            with found as (
              select id from app.outings
               where (@strava is not null and strava_id = @strava)
                  -- The same points at the same times are the same recording, under any Strava id.
                  or track_hash = @hash
                  -- Otherwise two Strava activities are never the same Outing, however close
                  -- their starts (two watches on one run); the nearest start wins.
                  or ((strava_id is null or @strava is null)
                      and @started is not null and started_at between @started - @same and @started + @same)
               order by (strava_id = @strava) desc nulls last, track_hash = @hash desc,
                        abs(extract(epoch from started_at - @started)) nulls last, id
               limit 1)
            update app.outings o
               set strava_id = coalesce(o.strava_id, @strava),
                   -- The archive's kind wins over a file's, but not over another activity's.
                   kind = case when @strava is null or o.strava_id <> @strava then o.kind else @kind::outing_kind end,
                   activity = case when @strava is null or o.strava_id <> @strava then o.activity else @activity::activity end
              from found where o.id = found.id
            returning o.id, (o.track_hash = @hash or (@strava is not null and o.strava_id = @strava)) as same
            """, connection, transaction))
        {
            AddCommon(existing.Parameters);
            existing.Parameters.Add(new NpgsqlParameter("same", SameStart));
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken)) return (reader.GetInt32(0), reader.GetBoolean(1) ? Adding.AlreadyThere : Adding.SameStart);
        }

        // Only when the Network is clipped to England and Wales: a regional one has nothing to test against.
        await using (var inside = new NpgsqlCommand("""
            select in_england_and_wales(ST_Transform(ST_SetSRID(ST_MakeLine(array(
                       select ST_MakePoint(lon, lat) from unnest(@lons, @lats) with ordinality as p(lon, lat, ord) order by ord)), 4326), 27700))
            """, connection, transaction))
        {
            inside.Parameters.Add(new NpgsqlParameter("lons", track.Points.Select(p => p.Longitude).ToArray()));
            inside.Parameters.Add(new NpgsqlParameter("lats", track.Points.Select(p => p.Latitude).ToArray()));
            if (!(bool)(await inside.ExecuteScalarAsync(cancellationToken))!) return (0, Adding.OutsideEnglandAndWales);
        }

        await using var insert = new NpgsqlCommand("""
            insert into app.outings (kind, activity, name, source_file, track_hash, started_at, strava_id, geom)
            select @kind::outing_kind, @activity::activity, @name, @file, @hash, @started, @strava,
                   ST_Transform(ST_SetSRID(ST_MakeLine(array(
                       select ST_MakePoint(lon, lat) from unnest(@lons, @lats) with ordinality as p(lon, lat, ord) order by ord)), 4326), 27700)
            returning id
            """, connection, transaction);
        AddCommon(insert.Parameters);
        insert.Parameters.Add(new NpgsqlParameter("name", name));
        insert.Parameters.Add(new NpgsqlParameter("file", file));
        insert.Parameters.Add(new NpgsqlParameter("lons", track.Points.Select(p => p.Longitude).ToArray()));
        insert.Parameters.Add(new NpgsqlParameter("lats", track.Points.Select(p => p.Latitude).ToArray()));
        return ((int)(await insert.ExecuteScalarAsync(cancellationToken))!, Adding.Added);

        void AddCommon(NpgsqlParameterCollection parameters)
        {
            parameters.Add(new NpgsqlParameter("strava", NpgsqlDbType.Bigint) { Value = (object?)stravaId ?? DBNull.Value });
            parameters.Add(new NpgsqlParameter("hash", hash));
            parameters.Add(new NpgsqlParameter("started", NpgsqlDbType.TimestampTz) { Value = (object?)track.StartedAt ?? DBNull.Value });
            parameters.Add(new NpgsqlParameter("kind", kind));
            parameters.Add(new NpgsqlParameter("activity", NpgsqlDbType.Text) { Value = (object?)ActivityOf(kind) ?? DBNull.Value });
        }
    }

    /// <summary>Works out what these Outings covered (db/functions/cover_outings.sql).</summary>
    internal static async Task CoverAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int[] ids, CancellationToken cancellationToken)
    {
        await using var cover = new NpgsqlCommand("select cover_outings(@ids)", connection, transaction) { CommandTimeout = 0 };
        cover.Parameters.Add(new NpgsqlParameter("ids", ids));
        await cover.ExecuteNonQueryAsync(cancellationToken);
    }

    // A recording has a time on every point; a planner's route (Trail Router's, Wend Your Way's own)
    // has a few or none, and importing it would count ground the user never ran.
    private static void RefusePlanned(RecordedTrack track)
    {
        if (track.Points.Count(p => p.Time is not null) * 2 < track.Points.Count)
            throw OutingSkips.Refused(OutingSkip.PlannedRoute, "it looks like a planned route, not a recorded run or ride: most of its points have no time");
    }

    /// <summary>Under this average moving speed, a track that doesn't say what it was is a run; at or over it, a ride (T133).</summary>
    public const double RunUnderKmPerHour = 15;

    // Moving: between points at this speed or more (2 km/h), so a stop doesn't slow a ride to a run.
    private const double MovingMetresPerSecond = 2 / 3.6;

    /// <summary>
    /// What an Outing was, from the activity type its file gives (Strava's, Garmin's and komoot's
    /// names); if it gives none (Footpath's don't), from its average moving speed (user chose
    /// "Judge by speed", T133): a run under 15 km/h, else a ride, as most of the user's cycling is
    /// on the road.
    /// </summary>
    /// <exception cref="InvalidDataException">It has no way on the ground, isn't something Wend Your Way knows, or says nothing and has too few times to tell.</exception>
    public static string KindOf(RecordedTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!string.IsNullOrEmpty(track.Type))
            return KindOf(track.Type) ?? throw new InvalidDataException($"it's a '{track.Type}', not a run, walk, hike or ride");
        return MovingKmPerHour(track) is { } speed
            ? speed < RunUnderKmPerHour ? "run" : "ride"
            : throw new InvalidDataException("it doesn't say what it was (a run, walk, hike or ride), and has too few times to tell from its speed");
    }

    // The distance covered while moving over the time spent moving; none if it never moved between timed points.
    private static double? MovingKmPerHour(RecordedTrack track)
    {
        var timed = track.Points.Where(p => p.Time is not null).ToList();
        double metres = 0, seconds = 0;
        foreach (var (a, b) in timed.Zip(timed.Skip(1)))
        {
            var step = (b.Time!.Value - a.Time!.Value).TotalSeconds;
            var along = new LatLon(a.Latitude, a.Longitude).MetresTo(new LatLon(b.Latitude, b.Longitude));
            if (step <= 0 || along / step < MovingMetresPerSecond) continue;
            (metres, seconds) = (metres + along, seconds + step);
        }
        return seconds > 0 ? metres / seconds * 3.6 : null;
    }

    /// <summary>
    /// What an Outing was, from an activity type as Strava's archive or a GPX, TCX or FIT file
    /// names it; null if it has no way on the ground (a swim, a ride on a trainer), isn't known, or isn't given.
    /// </summary>
    public static string? KindOf(string? type) => type?.Replace(" ", "_", StringComparison.Ordinal).ToUpperInvariant() switch
    {
        // A file that doesn't say could be a ride, and would count towards Run: skipped (user, 2026-10-18).
        null or "" => null,
        // komoot's: jogging, mountaineering, racebike, touringbicycle, citybike, mtb, mtb_easy (its gravel riding) (T133).
        "RUNNING" or "RUN" or "TRAIL_RUN" or "TRAIL_RUNNING" or "JOGGING" => "run",
        "WALKING" or "WALK" => "walk",
        "HIKING" or "HIKE" or "MOUNTAINEERING" => "hike",
        // Most of the user's cycling is on the road (user, 2026-10-18).
        "CYCLING" or "RIDE" or "ROAD_BIKING" or "ROAD_CYCLING"
            or "RACEBIKE" or "E_RACEBIKE" or "TOURINGBICYCLE" or "E_TOURINGBICYCLE" or "CITYBIKE" => "ride",
        "GRAVEL_RIDE" or "GRAVEL_CYCLING" or "MOUNTAIN_BIKING" or "MOUNTAIN_BIKE_RIDE" or "CYCLOCROSS"
            or "MTB" or "E_MTB" or "MTB_EASY" or "E_MTB_EASY" or "MTB_ADVANCED" or "E_MTB_ADVANCED" => "gravel_ride",
        _ => null,
    };

    /// <summary>
    /// The planning Activity an Outing of this kind informs: on foot, Run; a gravel ride, Gravel
    /// Ride; a road ride, none.
    /// </summary>
    public static string? ActivityOf(string kind) => kind switch
    {
        "run" or "walk" or "hike" => "run",
        "gravel_ride" => "gravel_ride",
        _ => null,
    };

    // The same points at the same times are the same track, whatever the file is called.
    private static string HashOf(RecordedTrack track)
    {
        var text = new StringBuilder();
        foreach (var p in track.Points)
            text.Append(CultureInfo.InvariantCulture, $"{p.Latitude:F7},{p.Longitude:F7},{p.Time:O};");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
