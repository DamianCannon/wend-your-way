using WendYourWay.Core.Network;
using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs;

/// <summary>
/// The app's own database, holding the Network `wend network build` made from a real extract.
/// Specs only ever read it: the connection refuses writes. Specs that need it are skipped when
/// no Network has been built.
/// </summary>
internal static class RealNetwork
{
    /// <summary>
    /// The Greater London Network the app read before it switched to Great Britain (T031), kept
    /// to compare against.
    /// </summary>
    public const string London = "cutthrough_london";

    /// <summary>The app's own database, which holds Great Britain's Network (T031).</summary>
    public const string GreatBritain = "cutthrough";

    /// <summary>
    /// A read-only data source on the real Network in the app's database, or in
    /// <paramref name="database"/>; or the calling spec is skipped.
    /// </summary>
    public static async Task<NpgsqlDataSource> OpenOrIgnore(string? database = null)
    {
        var readOnly = new NpgsqlConnectionStringBuilder(Database.DefaultConnectionString) { Options = "-c default_transaction_read_only=on" };
        // WEND_REAL_DATABASE points the specs at another built Network, to measure a rule on
        // London in the test database before a Great Britain build has it.
        database ??= Environment.GetEnvironmentVariable("WEND_REAL_DATABASE");
        if (database is not null) readOnly.Database = database;
        var dataSource = Database.CreateDataSource(readOnly.ToString());
        try
        {
            await using var count = dataSource.CreateCommand("select count(*) from network.segments");
            if ((long)(await count.ExecuteScalarAsync())! > 0) return dataSource;
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName or PostgresErrorCodes.InvalidCatalogName)
        {
        }
        await dataSource.DisposeAsync();
        Assert.Ignore($"No Network in {(database ?? "the app's database")}: run `wend network build` first.");
        return null!;
    }

    /// <summary>
    /// OS Terrain 50's tiles (T049) in WEND_TERRAIN, or data/terrain50/asc above wherever the
    /// specs run from; null when they aren't on this machine.
    /// </summary>
    public static TerrainGrid? Terrain()
    {
        if (Environment.GetEnvironmentVariable("WEND_TERRAIN") is { Length: > 0 } given && Directory.Exists(given)) return new TerrainGrid(given);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var folder = Path.Combine(dir.FullName, "data", "terrain50", "asc");
            if (Directory.Exists(folder)) return new TerrainGrid(folder);
        }
        return null;
    }

    /// <summary>A WGS84 position (a public landmark) in British National Grid.</summary>
    public static async Task<NodePoint> BritishNationalGrid(NpgsqlDataSource dataSource, double latitude, double longitude)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var query = dataSource.CreateCommand(
            "select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326), 27700) p) t");
        query.Parameters.Add(new NpgsqlParameter("lon", longitude));
        query.Parameters.Add(new NpgsqlParameter("lat", latitude));
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new NodePoint(reader.GetDouble(0), reader.GetDouble(1));
    }
}
