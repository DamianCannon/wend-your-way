using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>
/// Whether the last load of an extract into the osm schema finished (T127), in osm.import: an
/// import stopped part-way leaves osm half-loaded, and a Network rebuilt from it would lack what
/// it didn't load. The live Network isn't touched either way.
/// </summary>
public static class OsmImportMarker
{
    /// <summary>What a rebuild refused after an unfinished import says.</summary>
    public const string Unfinished =
        "The last import of an extract didn't finish, so a rebuild from it would miss what it didn't load. Run wend update, or wend network build <extract>.";

    public static Task ImportingAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default) => SetAsync(dataSource, "importing", cancellationToken);

    public static Task ImportedAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default) => SetAsync(dataSource, "imported", cancellationToken);

    /// <summary>Whether an import was begun and didn't finish; none begun since this was kept, no.</summary>
    public static async Task<bool> UnfinishedAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var query = dataSource.CreateCommand("select state from osm.import");
        return await query.ExecuteScalarAsync(cancellationToken) is "importing";
    }

    private static async Task SetAsync(NpgsqlDataSource dataSource, string state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var command = dataSource.CreateCommand("""
            insert into osm.import (state) values (@state)
            on conflict (id) do update set state = excluded.state, at = now()
            """);
        command.Parameters.Add(new NpgsqlParameter("state", state));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
