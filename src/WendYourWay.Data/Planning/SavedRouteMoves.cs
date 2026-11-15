using System.Globalization;
using WendYourWay.Core.Loops;
using Npgsql;

namespace WendYourWay.Data.Planning;

/// <summary>What became of a Saved Route offered a new map (T136).</summary>
public enum RouteMoveOutcome
{
    /// <summary>The map hasn't changed under it: nothing to do.</summary>
    Unchanged,

    /// <summary>Moved onto the map as it is: it follows the same line.</summary>
    Moved,

    /// <summary>Kept as it was: on the map as it is, it wouldn't follow the same line.</summary>
    Kept,
}

/// <summary>What became of a Saved Route offered a new map, and, kept, where it parts from the map (T136).</summary>
public sealed record RouteMove(RouteMoveOutcome Outcome, string? Said = null);

/// <summary>Saved Routes moved onto the map as it is (T136).</summary>
public interface ISavedRouteMoves
{
    /// <summary>The Saved Route moved onto the map as it is, if the map has changed under it and it would follow the same line.</summary>
    Task<RouteMove> MoveAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// A Saved Route moved onto the map as it is (PLAN T136, STORY-016; user: "I will want to be able
/// to nudge these in due course"). Its Segments are kept by OSM identity (T080), so a way split or
/// joined since flags it (T103), and it can't be nudged (T085), though every path of it is still
/// there. Its saved line is matched onto the Network as an imported GPX is (T083), for its own
/// Activity; moved if the new route follows the same line (no gap, each line within 25 m of the
/// other, 50 m where the old was off the map, lengths within 2%), its name, date and the user's stars kept. Otherwise kept as it was,
/// saying from where it no longer follows the map.
/// </summary>
public sealed class SavedRouteMoves(ISavedRoutes store, ILoopPlanner planner, NpgsqlDataSource dataSource) : ISavedRouteMoves
{
    /// <summary>How far either line may stray from the other and still be the same route.</summary>
    public const double WithinM = 25;

    /// <summary>
    /// How far the new route may be from the old where the old was off the map when saved, its GPS
    /// trace and not a mapped path (user, 2026-11-12, "50 m on old gaps": a path mapped since lay
    /// 34 m from their trace under trees).
    /// </summary>
    public const double OnOldGapsWithinM = 50;

    /// <summary>How much the lengths may differ and still be the same route.</summary>
    public const double LengthWithin = 0.02;

    public async Task<RouteMove> MoveAsync(int id, CancellationToken cancellationToken = default)
    {
        if ((await store.ListAsync(cancellationToken)).FirstOrDefault(r => r.Id == id) is not { MapChanged: true }) return new RouteMove(RouteMoveOutcome.Unchanged);
        if (await store.OpenAsync(id, cancellationToken) is not { } saved) return new RouteMove(RouteMoveOutcome.Unchanged);
        var old = saved.Loop;

        IOfferedLoops matched;
        try
        {
            matched = await planner.ImportAsync(old.Line, await ActivityAsync(id, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is RouteNotMatchedException or OutsideEnglandAndWalesException)
        {
            return Kept(0);
        }
        if (matched.Count == 0 || matched.TraversalsOf(matched[0]) is not { } traversals) return Kept(0);
        var moved = matched[0];

        // A gap is where the route leaves the map: it parts there, if not before.
        var (same, partsAtM) = await CompareAsync(old.Line, old.Gaps, moved.Line, moved.Gaps.Count == 0 ? null : moved.Gaps[0].Line[0], cancellationToken);
        if (!same) return Kept(partsAtM ?? 0);
        await store.MoveAsync(id, moved, traversals, matched.ScoreOf(moved), cancellationToken);
        return new RouteMove(RouteMoveOutcome.Moved);
    }

    /// <summary>Every Saved Route the map has changed under offered it as it is (after a build): how many moved, and how many were kept as they were.</summary>
    public async Task<(int Moved, int Kept)> MoveChangedAsync(Action<string, RouteMove>? said = null, CancellationToken cancellationToken = default)
    {
        var (moved, kept) = (0, 0);
        foreach (var route in (await store.ListAsync(cancellationToken)).Where(r => r.MapChanged))
        {
            var move = await MoveAsync(route.Id, cancellationToken);
            if (move.Outcome == RouteMoveOutcome.Moved) moved++;
            if (move.Outcome == RouteMoveOutcome.Kept) kept++;
            said?.Invoke(route.Name, move);
        }
        return (moved, kept);
    }

    private static RouteMove Kept(double atM) =>
        new(RouteMoveOutcome.Kept, string.Format(CultureInfo.InvariantCulture, "From {0:0.0} km it no longer follows the map.", atM / 1000));

    private async Task<Activity> ActivityAsync(int id, CancellationToken cancellationToken)
    {
        await using var query = dataSource.CreateCommand("select activity::text from app.saved_routes where id = @id");
        query.Parameters.Add(new NpgsqlParameter("id", id));
        return ActivityColumn.OfSavedRoute(await query.ExecuteScalarAsync(cancellationToken) as string);
    }

    // Whether the two lines are the same route: each within 25 m of the other (50 m on the old one's
    // gaps), their lengths within 2%, no gap; and how far along the old one they first part, or the
    // gap starts, if either.
    private async Task<(bool Same, double? PartsAtM)> CompareAsync(
        IReadOnlyList<LatLon> old, IReadOnlyList<RouteGap> oldGaps, IReadOnlyList<LatLon> other, LatLon? gap, CancellationToken cancellationToken)
    {
        await using var query = dataSource.CreateCommand("""
            with lines as (
              select (select ST_Transform(ST_SetSRID(ST_MakeLine(array(
                        select ST_MakePoint(lon, lat) from unnest(@oldLons, @oldLats) with ordinality as p(lon, lat, ord) order by ord)), 4326), 27700)) as a,
                     (select ST_Transform(ST_SetSRID(ST_MakeLine(array(
                        select ST_MakePoint(lon, lat) from unnest(@lons, @lats) with ordinality as p(lon, lat, ord) order by ord)), 4326), 27700)) as b),
            gaps as (
              select ST_Transform(ST_SetSRID(ST_MakeLine(array_agg(ST_MakePoint(lon, lat) order by ord)), 4326), 27700) as g
                from unnest(@gapIds::int[], @gapLons::float8[], @gapLats::float8[]) with ordinality as p(gap, lon, lat, ord)
               group by gap having count(*) > 1),
            -- Where the old route was off the map: 50 m of its trace, and the old line along it.
            off_map as (
              select coalesce(ST_Union(ST_Buffer(g, @gapWithin)), ST_GeomFromText('POLYGON EMPTY', 27700)) as near,
                     coalesce(ST_Union(ST_Buffer(g, 1)), ST_GeomFromText('POLYGON EMPTY', 27700)) as along
                from gaps),
            astray as (
              select d.geom from lines, off_map,
                     ST_Dump(ST_Difference(a, ST_Union(ST_Buffer(b, @within), ST_Intersection(along, ST_Buffer(b, @gapWithin))))) d
              union all
              select d.geom from lines, off_map, ST_Dump(ST_Difference(b, ST_Union(ST_Buffer(a, @within), near))) d)
            select abs(ST_Length(b) - ST_Length(a)) <= @lengthWithin * ST_Length(a),
                   least(
                     (select min(ST_LineLocatePoint(a, ST_StartPoint(astray.geom))) from astray where ST_Length(astray.geom) > 0),
                     case when @gapLon::float8 is not null
                          then ST_LineLocatePoint(a, ST_Transform(ST_SetSRID(ST_MakePoint(@gapLon::float8, @gapLat::float8), 4326), 27700)) end) * ST_Length(a)
              from lines
            """);
        query.Parameters.Add(new NpgsqlParameter("oldLons", old.Select(p => p.Longitude).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("oldLats", old.Select(p => p.Latitude).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("lons", other.Select(p => p.Longitude).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("lats", other.Select(p => p.Latitude).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("within", WithinM));
        query.Parameters.Add(new NpgsqlParameter("gapWithin", OnOldGapsWithinM));
        query.Parameters.Add(new NpgsqlParameter("gapIds", oldGaps.SelectMany((g, i) => g.Line.Select(_ => i)).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("gapLons", oldGaps.SelectMany(g => g.Line.Select(p => p.Longitude)).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("gapLats", oldGaps.SelectMany(g => g.Line.Select(p => p.Latitude)).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("lengthWithin", LengthWithin));
        query.Parameters.Add(new NpgsqlParameter("gapLon", NpgsqlTypes.NpgsqlDbType.Double) { Value = (object?)gap?.Longitude ?? DBNull.Value });
        query.Parameters.Add(new NpgsqlParameter("gapLat", NpgsqlTypes.NpgsqlDbType.Double) { Value = (object?)gap?.Latitude ?? DBNull.Value });
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var partsAtM = reader.IsDBNull(1) ? (double?)null : reader.GetDouble(1);
        return (reader.GetBoolean(0) && partsAtM is null, partsAtM);
    }
}
