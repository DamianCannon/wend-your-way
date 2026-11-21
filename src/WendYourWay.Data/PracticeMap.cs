using Npgsql;
using WendYourWay.Data.Building;
using WendYourWay.Data.UserData;

namespace WendYourWay.Data;

/// <summary>
/// The practice map (T161, #165; user: "ok so now the demo map?"): Richmond Park and around, from the
/// OpenStreetMap extract the specs build on, shipped with the app; built in a database of its own beside
/// the app's, to try Wend Your Way on before building a region, or to show it without touching your own
/// map or runs. What's done there is emptied each time it opens; its map stays.
/// </summary>
public static class PracticeMap
{
    /// <summary>What a practice database's name ends with, after the app's own.</summary>
    public const string Suffix = "_practice";

    /// <summary>Where Requests start on it, whatever Home is set: Pembroke Lodge, in the park.</summary>
    public static readonly LatLon Start = new(51.4436, -0.2963);

    /// <summary>The extract shipped beside the program.</summary>
    public static string Extract => Path.Combine(AppContext.BaseDirectory, "practice", "richmond-park.osm.gz");

    /// <summary>The practice database beside the app's own: its name with <see cref="Suffix"/>; a spec's test database is its own.</summary>
    public static string ConnectionStringOf(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.Database is { } database && !database.EndsWith(Suffix, StringComparison.Ordinal) && !database.EndsWith("_test", StringComparison.Ordinal)) builder.Database = database + Suffix;
        return builder.ConnectionString;
    }

    /// <summary>Why a database that isn't a practice one (or a spec's) isn't built over or emptied.</summary>
    public static string NotPractice(string? database) =>
        $"'{database}' isn't a practice map's database: only one named …{Suffix} is built over or emptied.";

    // Only a practice database, or a spec's, is ever built over or emptied: never the app's own.
    private static void Guard(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (database is null || !(database.EndsWith(Suffix, StringComparison.Ordinal) || database.EndsWith("_test", StringComparison.Ordinal)))
            throw new InvalidOperationException(NotPractice(database));
    }

    /// <summary>Whether its map is there: a Network of Segments.</summary>
    public static async Task<bool> IsBuiltAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var dataSource = Database.CreateDataSource(connectionString);
        await using var query = dataSource.CreateCommand(
            "select to_regclass('network.segments') is not null and exists (select 1 from network.segments)");
        try
        {
            return (bool)(await query.ExecuteScalarAsync(cancellationToken))!;
        }
        catch (PostgresException)
        {
            // network.segments named but not there: no map.
            return false;
        }
    }

    /// <summary>
    /// Builds it, over whatever was there: everything the app's role owns in the database dropped,
    /// migrated, the extract loaded, and its Network built (no byways: none are near it).
    /// </summary>
    public static async Task BuildAsync(string connectionString, string osm2pgsqlExe, string extract, CancellationToken cancellationToken = default)
    {
        Guard(connectionString);
        await using (var dataSource = Database.CreateDataSource(connectionString))
        {
            var role = new NpgsqlConnectionStringBuilder(connectionString).Username;
            await using var drop = dataSource.CreateCommand($"drop owned by \"{role}\"");
            await drop.ExecuteNonQueryAsync(cancellationToken);
            // Where the role is the superuser (Docker's), PostGIS was its own, and went too: put back.
            await using var postgis = dataSource.CreateCommand("create extension if not exists postgis");
            await postgis.ExecuteNonQueryAsync(cancellationToken);
        }
        Migrations.Apply(connectionString);
        OsmImport.Run(osm2pgsqlExe, connectionString, extract, cancellationToken: cancellationToken);
        await using var built = Database.CreateDataSource(connectionString);
        await NetworkBuild.Build(built, cancellationToken: cancellationToken);
    }

    /// <summary>What was done there before, gone: its Saved Routes, Marks, Outings and settings; its map stays.</summary>
    public static async Task EmptyAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        Guard(connectionString);
        await using var dataSource = Database.CreateDataSource(connectionString);
        var tables = await UserDataBackups.TablesAsync(dataSource, cancellationToken);
        if (tables.Count == 0) return;
        await using var empty = dataSource.CreateCommand($"truncate {string.Join(", ", tables.Select(t => "app." + t))} restart identity cascade");
        await empty.ExecuteNonQueryAsync(cancellationToken);
    }
}
