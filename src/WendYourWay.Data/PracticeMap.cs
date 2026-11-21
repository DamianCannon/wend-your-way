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

    // What the app made in the database, dropped: its schemas, and in public its functions, tables and
    // types (the migrations' journal among them); never an extension's (PostGIS's), nor the database's own.
    // Not `drop owned`, which PostgreSQL refuses for a superuser, as Docker's role is.
    private const string DropTheApps = """
        do $$
        declare r record;
        begin
          for r in select nspname from pg_namespace
                   where nspowner = current_user::regrole and nspname <> 'public' and nspname not like 'pg\_%' and nspname <> 'information_schema'
          loop
            execute format('drop schema %I cascade', r.nspname);
          end loop;
          for r in select p.oid::regprocedure as name from pg_proc p join pg_namespace n on n.oid = p.pronamespace
                   where n.nspname = 'public' and p.proowner = current_user::regrole
                     and not exists (select 1 from pg_depend d where d.objid = p.oid and d.deptype = 'e')
          loop
            execute format('drop function if exists %s cascade', r.name);
          end loop;
          for r in select c.oid::regclass as name, c.relkind from pg_class c join pg_namespace n on n.oid = c.relnamespace
                   where n.nspname = 'public' and c.relowner = current_user::regrole and c.relkind in ('r', 'p', 'v', 'm')
                     and not exists (select 1 from pg_depend d where d.objid = c.oid and d.deptype = 'e')
          loop
            execute format('drop %s if exists %s cascade',
              case r.relkind when 'v' then 'view' when 'm' then 'materialized view' else 'table' end, r.name);
          end loop;
          for r in select t.oid::regtype as name from pg_type t join pg_namespace n on n.oid = t.typnamespace
                   where n.nspname = 'public' and t.typowner = current_user::regrole and t.typtype in ('e', 'c', 'd') and t.typelem = 0
                     and (t.typrelid = 0 or (select relkind from pg_class where oid = t.typrelid) = 'c')
                     and not exists (select 1 from pg_depend d where d.objid = t.oid and d.deptype = 'e')
          loop
            execute format('drop type if exists %s cascade', r.name);
          end loop;
        end $$
        """;

    /// <summary>
    /// Builds it, over whatever was there: what the app made in the database dropped, migrated, the
    /// extract loaded, and its Network built (no byways: none are near it).
    /// </summary>
    public static async Task BuildAsync(string connectionString, string osm2pgsqlExe, string extract, CancellationToken cancellationToken = default)
    {
        Guard(connectionString);
        await using (var dataSource = Database.CreateDataSource(connectionString))
        {
            await using var drop = dataSource.CreateCommand(DropTheApps);
            await drop.ExecuteNonQueryAsync(cancellationToken);
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
