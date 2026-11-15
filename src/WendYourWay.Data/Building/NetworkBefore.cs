using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>
/// The map before the one in use (#135, T131): when its data is from, and the one in use's (none
/// for one built before T117 kept it), and whether this Wend Your Way can read it.
/// </summary>
public sealed record MapBefore(DateTimeOffset? At, DateTimeOffset? InUseAt, bool Fits)
{
    /// <summary>Whether it's the newer of the two, so going to it is going forward: a map with no date counts as older than one with a date.</summary>
    public bool Newer => At is { } at && (InUseAt is not { } inUse || at > inUse);
}

/// <summary>A map before that this Wend Your Way can't go back to: none, or of an older make.</summary>
public sealed class NoMapBeforeException(string message) : Exception(message);

/// <summary>
/// Back to the map before (#135, T131, STORY-048): network_prev, kept by the last build (T034),
/// swapped with network in one transaction, so readers see one map or the other, never neither.
/// Done again, it's forward again.
/// </summary>
public static class NetworkBefore
{
    public const string None = "There's no map before this one to go back to.";

    public const string OlderMake = "The map before this one was made by an older Wend Your Way, so it can't be gone back to.";

    /// <summary>
    /// The map before, if there is one: network_prev with Segments in it (a fresh database's first
    /// build leaves the empty Network the migrations made). It fits if its tables have the same
    /// columns and indexes as network's: a migration since may have changed them.
    /// </summary>
    public static async Task<MapBefore?> FindAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await FindAsync(connection, null, cancellationToken);
    }

    private static async Task<MapBefore?> FindAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using (var exists = new NpgsqlCommand("select to_regclass('network_prev.segments') is not null", connection, transaction))
            if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken))!) return null;
        await using var query = new NpgsqlCommand("""
            select exists (select 1 from network_prev.segments),
                   (select osm_data_at from network_prev.built_from limit 1),
                   (select osm_data_at from network.built_from limit 1),
                   not exists (
                     (select table_name, column_name, data_type, is_nullable, coalesce(generation_expression, '') from information_schema.columns where table_schema = 'network'
                      except select table_name, column_name, data_type, is_nullable, coalesce(generation_expression, '') from information_schema.columns where table_schema = 'network_prev')
                     union all
                     (select table_name, column_name, data_type, is_nullable, coalesce(generation_expression, '') from information_schema.columns where table_schema = 'network_prev'
                      except select table_name, column_name, data_type, is_nullable, coalesce(generation_expression, '') from information_schema.columns where table_schema = 'network'))
                   and not exists (
                     (select tablename, indexname, replace(indexdef, ' ON network.', ' ON x.'), '' from pg_indexes where schemaname = 'network'
                      except select tablename, indexname, replace(indexdef, ' ON network_prev.', ' ON x.'), '' from pg_indexes where schemaname = 'network_prev')
                     union all
                     (select tablename, indexname, replace(indexdef, ' ON network_prev.', ' ON x.'), '' from pg_indexes where schemaname = 'network_prev'
                      except select tablename, indexname, replace(indexdef, ' ON network.', ' ON x.'), '' from pg_indexes where schemaname = 'network'))
            """, connection, transaction);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (!reader.GetBoolean(0)) return null;
        return new MapBefore(At(reader, 1), At(reader, 2), reader.GetBoolean(3));
    }

    private static DateTimeOffset? At(NpgsqlDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(column), DateTimeKind.Utc));

    /// <summary>
    /// Swaps the map before in, and works out again what each Outing covered on it, its scores
    /// cleared to be scored again (as a build does); in one transaction. What was swapped in.
    /// </summary>
    /// <exception cref="NoMapBeforeException">There's none, or it's of an older make.</exception>
    public static async Task<MapBefore> SwapAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await FindAsync(connection, transaction, cancellationToken) ?? throw new NoMapBeforeException(None);
        if (!before.Fits) throw new NoMapBeforeException(OlderMake);

        foreach (var sql in new[]
        {
            "alter schema network rename to network_swapping; alter schema network_prev rename to network; alter schema network_swapping rename to network_prev",
            // Segment identities differ between maps, so coverage is derived afresh (ADR 0005).
            "select cover_outings()",
            "update app.outings set score = null, matched_share = null, off_road_share = null, climb_per_km = null",
            "analyze network.segments; analyze network.segment_beside; analyze network.offroad_networks; analyze network.segment_offroad; analyze network.trailheads; analyze network.segment_obstacles",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return before;
    }
}
