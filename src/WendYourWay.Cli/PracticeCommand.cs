using System.Diagnostics;
using System.Globalization;
using Npgsql;
using WendYourWay.Data;

namespace WendYourWay.Cli;

/// <summary>
/// `wend practice` (T161, #165): the practice map, Richmond Park and around, built in its own database
/// beside the app's if it isn't there already (or with --rebuild, again). Its database made here where the
/// role may (Docker's); elsewhere scripts/setup.ps1 makes it, as it makes the specs'.
/// </summary>
public static class PracticeCommand
{
    /// <summary>Said when there's no practice database and this role can't make one.</summary>
    public const string NoDatabase = "There's no practice map database yet, and this database role can't make one. Run scripts/setup.ps1 again, as administrator, to add it.";

    public static async Task<int> Run(string connectionString, string? osm2pgsqlExe, bool rebuild, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var practice = PracticeMap.ConnectionStringOf(connectionString);
        if (!await ThereAsync(practice, output, cancellationToken)) return 1;
        if (!rebuild && await PracticeMap.IsBuiltAsync(practice, cancellationToken))
        {
            var segments = await SegmentsAsync(practice, cancellationToken);
            await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"The practice map is ready: Richmond Park and around, {segments:N0} Segments."));
            return 0;
        }
        if (!File.Exists(PracticeMap.Extract))
        {
            await output.WriteLineAsync($"No practice map extract at {PracticeMap.Extract}: build Wend Your Way again.");
            return 1;
        }
        if (osm2pgsqlExe is null || !File.Exists(osm2pgsqlExe))
        {
            await output.WriteLineAsync("osm2pgsql not found. Run scripts/setup.ps1, or pass --osm2pgsql <path>.");
            return 1;
        }
        await output.WriteLineAsync("Building the practice map: Richmond Park and around …");
        var clock = Stopwatch.StartNew();
        await PracticeMap.BuildAsync(practice, osm2pgsqlExe, PracticeMap.Extract, cancellationToken);
        var built = await SegmentsAsync(practice, cancellationToken);
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"The practice map is ready: Richmond Park and around, {built:N0} Segments, in {clock.Elapsed.TotalSeconds:0} s."));
        return 0;
    }

    // Its database there, or made: where the role may make databases, with PostGIS in it.
    private static async Task<bool> ThereAsync(string practice, TextWriter output, CancellationToken cancellationToken)
    {
        var name = new NpgsqlConnectionStringBuilder(practice).Database!;
        var server = new NpgsqlConnectionStringBuilder(practice) { Database = "postgres", Pooling = false }.ConnectionString;
        try
        {
            await using var connection = new NpgsqlConnection(server);
            await connection.OpenAsync(cancellationToken);
            await using (var exists = new NpgsqlCommand("select exists (select 1 from pg_database where datname = @name)", connection))
            {
                exists.Parameters.AddWithValue("name", name);
                if ((bool)(await exists.ExecuteScalarAsync(cancellationToken))!) return true;
            }
            await using (var create = new NpgsqlCommand($"create database \"{name}\"", connection))
                await create.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.InsufficientPrivilege)
        {
            await output.WriteLineAsync(NoDatabase);
            return false;
        }
        await using var made = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(practice) { Pooling = false }.ConnectionString);
        await made.OpenAsync(cancellationToken);
        await using var postgis = new NpgsqlCommand("create extension if not exists postgis", made);
        await postgis.ExecuteNonQueryAsync(cancellationToken);
        await output.WriteLineAsync($"Made the practice map's database, {name}.");
        return true;
    }

    private static async Task<long> SegmentsAsync(string practice, CancellationToken cancellationToken)
    {
        await using var dataSource = Database.CreateDataSource(practice);
        await using var count = dataSource.CreateCommand("select count(*) from network.segments");
        return (long)(await count.ExecuteScalarAsync(cancellationToken))!;
    }
}
