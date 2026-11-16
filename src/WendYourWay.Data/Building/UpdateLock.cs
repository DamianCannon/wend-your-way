using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>
/// One map update or Network build at a time (T127): a PostgreSQL advisory lock, held by a
/// connection of its own for as long as it's kept. It goes when it's given back or its process
/// ends, so a run that dies never leaves it held.
/// </summary>
public sealed class UpdateLock : IAsyncDisposable
{
    // "CTMAP": the same for `wend update`, `wend network build` and `wend network rebuild`.
    private const long Key = 0x43_54_4D_41_50;

    private readonly NpgsqlConnection _connection;

    private UpdateLock(NpgsqlConnection connection) => _connection = connection;

    /// <summary>The lock, or none if an update or a build already holds it.</summary>
    public static async Task<UpdateLock?> TryTakeAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        // Not pooled: a pooled connection given back keeps its session, and the lock with it.
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var take = new NpgsqlCommand("select pg_try_advisory_lock(@key)", connection))
        {
            take.Parameters.Add(new NpgsqlParameter("key", Key));
            if ((bool)(await take.ExecuteScalarAsync(cancellationToken))!) return new UpdateLock(connection);
        }
        await connection.DisposeAsync();
        return null;
    }

    /// <summary>
    /// Whether an update, a build or another job holds it (#153, T149): seen in pg_locks, not by taking it,
    /// so a run starting just then isn't refused for the look. A bigint key is held as its two halves.
    /// </summary>
    public static async Task<bool> IsHeldAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var look = new NpgsqlCommand("""
            select exists (select 1 from pg_locks
                            where locktype = 'advisory' and granted and database = (select oid from pg_database where datname = current_database())
                              and classid::bigint = @high and objid::bigint = @low and objsubid = 1)
            """, connection);
        look.Parameters.Add(new NpgsqlParameter("high", Key >> 32));
        look.Parameters.Add(new NpgsqlParameter("low", Key & 0xFFFF_FFFF));
        return (bool)(await look.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>What a run refused for the lock says.</summary>
    public const string Held = "A map update or a Network build is already running: try again when it's done.";

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
