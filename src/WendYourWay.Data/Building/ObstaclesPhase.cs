using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>What finding the Obstacles did (T069): how many Segments have each kind.</summary>
public sealed record ObstacleBuild(IReadOnlyDictionary<string, long> SegmentsByKind)
{
    public long Segments => SegmentsByKind.Values.Sum();
}

/// <summary>
/// The build's Obstacles phase (PLAN T069): the barrier nodes and fords on each Segment, by
/// find_obstacles(), after the Segments and before they're put in order of place.
/// </summary>
public static class ObstaclesPhase
{
    /// <summary>
    /// Finds the Obstacles of the Network in <paramref name="schema"/> (network_next in a build,
    /// network on its own) in place of any found before, within <paramref name="transaction"/>.
    /// </summary>
    public static async Task<ObstacleBuild> Build(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (schema is not ("network" or "network_next")) throw new ArgumentOutOfRangeException(nameof(schema), schema, "Only network or network_next.");

        var found = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("select kind, found from find_obstacles(@schema)", connection, transaction) { CommandTimeout = 0 })
        {
            command.Parameters.Add(new NpgsqlParameter("schema", schema));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) found[reader.GetString(0)] = reader.GetInt64(1);
        }
        await using (var command = new NpgsqlCommand($"analyze {schema}.segment_obstacles", connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return new ObstacleBuild(found);
    }
}
