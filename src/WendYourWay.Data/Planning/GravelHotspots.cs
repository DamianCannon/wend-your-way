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
