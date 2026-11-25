using Npgsql;

namespace WendYourWay.Data.Planning;

/// <summary>The Gravel hotspots of the whole Network (T072).</summary>
public interface IGravelHotspots
{
    /// <summary>Every Gravel Off-road network of <paramref name="fromKm"/> or more, largest first.</summary>
    Task<IReadOnlyList<Hotspot>> AllAsync(double fromKm = PostgisGravelHotspots.FromKm, CancellationToken cancellationToken = default);
}

/// <summary>Reads the Gravel hotspots from network.gravel_networks.</summary>
public sealed class PostgisGravelHotspots(NpgsqlDataSource dataSource) : IGravelHotspots
{
    /// <summary>
    /// A hotspot's least size (T072): 505 across Great Britain, 75 within 80 km of London; lowered
    /// from 50 km (229 and 29) by the user for more options near London (2026-11-06).
    /// </summary>
    public const double FromKm = 25;

    public Task<IReadOnlyList<Hotspot>> AllAsync(double fromKm = FromKm, CancellationToken cancellationToken = default) =>
        HotspotTable.ReadAsync(dataSource, "gravel_networks", fromKm, cancellationToken);
}

/// <summary>
/// A table of Hotspots the build keeps (T188): gravel_networks (T072), trail_networks (T189); each its metres, a
/// junction to mark it at, its box and the nearest town or village.
/// </summary>
internal static class HotspotTable
{
    /// <summary>Those of <paramref name="fromKm"/> or more, largest first; none on a Network built before the table was.</summary>
    public static async Task<IReadOnlyList<Hotspot>> ReadAsync(NpgsqlDataSource dataSource, string table, double fromKm, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        if (!await ExistsAsync(dataSource, table, cancellationToken)) return [];
        await using var query = dataSource.CreateCommand($"""
            select near, ST_Y(p), ST_X(p), metres / 1000, ST_YMin(b), ST_XMin(b), ST_YMax(b), ST_XMax(b)
              from (select near, metres, ST_Transform(at, 4326) p, ST_Transform(box, 4326) b
                      from network.{table} where metres >= @from) g
             order by metres desc
            """);
        query.Parameters.Add(new NpgsqlParameter("from", fromKm * 1000));
        var hotspots = new List<Hotspot>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hotspots.Add(new Hotspot(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                new LatLon(reader.GetDouble(1), reader.GetDouble(2)),
                reader.GetDouble(3),
                new MapBounds(reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7))));
        }
        return hotspots;
    }

    /// <summary>Whether the Network has this table, and any rows in it (T189: one built before it has neither).</summary>
    public static async Task<bool> HasRowsAsync(NpgsqlDataSource dataSource, string table, CancellationToken cancellationToken) =>
        await ExistsAsync(dataSource, table, cancellationToken) && await AnyAsync(dataSource, table, cancellationToken);

    private static async Task<bool> ExistsAsync(NpgsqlDataSource dataSource, string table, CancellationToken cancellationToken)
    {
        await using var exists = dataSource.CreateCommand($"select to_regclass('network.{table}') is not null");
        return (bool)(await exists.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> AnyAsync(NpgsqlDataSource dataSource, string table, CancellationToken cancellationToken)
    {
        await using var any = dataSource.CreateCommand($"select exists (select 1 from network.{table})");
        return (bool)(await any.ExecuteScalarAsync(cancellationToken))!;
    }
}

/// <summary>
/// Run hotspots, from the squares the build counted (T190): those holding <c>fromKm</c> or more, joined where they
/// lie side by side (not those touching only at a corner: one area ran 45 km across so) into areas, largest first;
/// each its km together, its box, marked at its densest square's
/// junction and named by the town or village nearest that. A few hundred squares, joined as they're read.
/// </summary>
internal static class RunSquares
{
    public static async Task<IReadOnlyList<Hotspot>> ReadAsync(NpgsqlDataSource dataSource, double fromKm, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        bool named;
        await using (var exists = dataSource.CreateCommand("select to_regclass('network.run_squares') is not null, to_regclass('places.names') is not null"))
        await using (var found = await exists.ExecuteReaderAsync(cancellationToken))
        {
            await found.ReadAsync(cancellationToken);
            if (!found.GetBoolean(0)) return [];
            named = found.GetBoolean(1);
        }
        var near = named
            ? "(select p.name from places.names p where p.kind in ('City', 'Town', 'Village') order by p.geom <-> a.at limit 1)"
            : "null::text";
        await using var query = dataSource.CreateCommand($"""
            -- Side by side: their middles a side apart; at a corner, 1.4 sides.
            with side as (select max(ST_XMax(box) - ST_XMin(box)) as m from network.run_squares),
                 dense as (select metres, box, at, ST_ClusterDBSCAN(ST_Centroid(box), eps := (select m * 1.1 from side), minpoints := 1) over () as joined
                             from network.run_squares where metres >= @from),
                 areas as (select sum(metres) as metres,
                                  ST_MakeEnvelope(min(ST_XMin(box)), min(ST_YMin(box)), max(ST_XMax(box)), max(ST_YMax(box)), 27700) as box,
                                  (array_agg(at order by metres desc))[1] as at
                             from dense group by joined)
            select near, ST_Y(p), ST_X(p), metres / 1000, ST_YMin(b), ST_XMin(b), ST_YMax(b), ST_XMax(b)
              from (select {near} as near, a.metres, ST_Transform(a.at, 4326) p, ST_Transform(a.box, 4326) b from areas a) g
             order by metres desc
            """);
        query.Parameters.Add(new NpgsqlParameter("from", fromKm * 1000));
        var hotspots = new List<Hotspot>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hotspots.Add(new Hotspot(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                new LatLon(reader.GetDouble(1), reader.GetDouble(2)),
                reader.GetDouble(3),
                new MapBounds(reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7))));
        }
        return hotspots;
    }
}
