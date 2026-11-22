using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace WendYourWay.Data.UserData;

/// <summary>What a backup holds: when it was taken, the last migration applied then, and each `app` table's rows.</summary>
public sealed record BackupManifest(DateTimeOffset TakenAt, string Migration, IReadOnlyDictionary<string, long> Rows);

/// <summary>A backup: its dump file, and its manifest beside it.</summary>
public sealed record UserDataBackup(string DumpPath, BackupManifest Manifest);

/// <summary>What a restore did: the backup of what was there first, each table's rows, expected and restored, and when the backup put back was taken.</summary>
public sealed record RestoreReport(UserDataBackup Before, IReadOnlyDictionary<string, (long Expected, long Restored)> Rows, DateTimeOffset TakenAt);

/// <summary>A backup taken by a newer Wend Your Way than the database: its migrations aren't all applied here.</summary>
public sealed class BackupTooNewException(string message) : Exception(message);

/// <summary>
/// The user's own data backed up and restored (PLAN T102, STORY-031; user: "OneDrive",
/// "Automatically too"): the `app` schema by PostgreSQL's own pg_dump, custom format, to a dated
/// file with a manifest beside it; restored by pg_restore into the tables as they are now, after a
/// backup of what was there, so a backup outlives later migrations. 10 kept, spread over a month (T134).
/// </summary>
public static class UserDataBackups
{
    /// <summary>How many backups are kept in the folder, at most: the newest, then a day's, then a week's (T134).</summary>
    public const int Kept = Newest + Days + Weeks;

    private const int Newest = 3;
    private const int Days = 4;
    private const int Weeks = 3;

    /// <summary>The folder's name in the user's OneDrive, when no folder is set.</summary>
    public const string OneDriveFolder = "Wend Your Way Backups";

    private const string Prefix = "wend-your-way-";

    /// <summary>How backups were named before the rename (T140): still listed, kept, found and restorable.</summary>
    private const string FormerPrefix = "cutthrough-app-";

    // Every backup of either naming in the folder.
    private static IEnumerable<string> Dumps(string folder) =>
        Directory.EnumerateFiles(folder, Prefix + "*.dump").Concat(Directory.EnumerateFiles(folder, FormerPrefix + "*.dump"));

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The folder set, or Wend Your Way Backups in OneDrive; none without either.</summary>
    public static string? FolderFor(string? configured, string? oneDrive) =>
        !string.IsNullOrWhiteSpace(configured) ? configured
        : !string.IsNullOrWhiteSpace(oneDrive) ? Path.Combine(oneDrive, OneDriveFolder)
        : null;

    /// <summary>This laptop's backup folder: Backup:Folder if set, else Wend Your Way Backups in OneDrive.</summary>
    public static string? Folder(string? configured = null) => FolderFor(configured, Environment.GetEnvironmentVariable("OneDrive"));

    /// <summary>
    /// Where a database's own backups go: the app's own database, straight into the folder; any
    /// other (London's, say) into a folder of its own inside it, so keeping the last 10 of one never
    /// throws away the other's; a test database, nowhere: it's never backed up by itself.
    /// </summary>
    public static string? FolderOf(string? folder, string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? "";
        return folder is null || database.EndsWith("_test", StringComparison.Ordinal) || database.EndsWith(PracticeMap.Suffix, StringComparison.Ordinal) ? null
            : database == new NpgsqlConnectionStringBuilder(Database.DefaultConnectionString).Database ? folder
            : Path.Combine(folder, database);
    }

    public static async Task<UserDataBackup> BackupAsync(string connectionString, string folder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Directory.CreateDirectory(folder);
        var now = DateTimeOffset.Now;
        // A second backup in the same minute gets a second of its own.
        var name = $"{Prefix}{now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}";
        if (File.Exists(Path.Combine(folder, name + ".dump"))) name += now.ToString("ss", CultureInfo.InvariantCulture);
        var dump = Path.Combine(folder, name + ".dump");

        // Written under another name and renamed when whole, the manifest last (T138): one cut off
        // (the app stopped, the laptop off) never looks like a backup, and isn't listed or pruned.
        var part = dump + ".part";
        try
        {
            await RunAsync("pg_dump", connectionString, ["--format=custom", "--schema=app", .. NotTheUsers.Select(t => $"--exclude-table-data=app.{t}"), "--no-owner", "--no-privileges", $"--file={part}"], cancellationToken);
            var manifest = new BackupManifest(now, await LastAppliedAsync(connectionString, cancellationToken), await RowsAsync(connectionString, cancellationToken));
            File.Move(part, dump);
            var manifestPath = Path.ChangeExtension(dump, ".json");
            await File.WriteAllTextAsync(manifestPath + ".part", JsonSerializer.Serialize(manifest, Json), cancellationToken);
            File.Move(manifestPath + ".part", manifestPath);
            return new UserDataBackup(dump, manifest);
        }
        finally
        {
            if (File.Exists(part)) File.Delete(part);
        }
    }

    /// <exception cref="BackupTooNewException">The backup's last migration isn't applied in this database.</exception>
    public static async Task<RestoreReport> RestoreAsync(string connectionString, string dumpPath, string backupFirstFolder, CancellationToken cancellationToken = default)
    {
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(Path.ChangeExtension(dumpPath, ".json"), cancellationToken), Json)
            ?? throw new InvalidDataException("That backup's manifest can't be read.");
        var here = await LastAppliedAsync(connectionString, cancellationToken);
        if (string.CompareOrdinal(Short(manifest.Migration), Short(here)) > 0)
            throw new BackupTooNewException($"That backup is from a newer Wend Your Way ({Short(manifest.Migration)}) than this database ({Short(here)}): update Wend Your Way first.");

        // What's there now, kept first: a restore can be undone.
        var before = await BackupAsync(connectionString, backupFirstFolder, cancellationToken);

        await using (var dataSource = Database.CreateDataSource(connectionString))
        {
            var tables = await TablesAsync(dataSource, cancellationToken);
            await using var empty = dataSource.CreateCommand($"truncate {string.Join(", ", tables.Select(t => "app." + t))} restart identity cascade");
            await empty.ExecuteNonQueryAsync(cancellationToken);
        }
        // Into the tables as they are now: a backup from before a later migration still fits. A
        // full dump lists its tables by name, so each table's rows are put in after those it
        // refers to (pg_restore -L restores in the list's order).
        var list = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(list, await ParentsFirstAsync(connectionString, await RunAsync("pg_restore", null, ["--list", dumpPath], cancellationToken), cancellationToken), cancellationToken);
            await RunAsync("pg_restore", connectionString, ["--data-only", "--schema=app", "--single-transaction", "--no-owner", "--no-privileges", $"--use-list={list}", dumpPath], cancellationToken);
        }
        finally
        {
            File.Delete(list);
        }

        var restored = await RowsAsync(connectionString, cancellationToken);
        return new RestoreReport(before, manifest.Rows.ToDictionary(r => r.Key, r => (r.Value, restored.GetValueOrDefault(r.Key))), manifest.TakenAt);
    }

    /// <summary>
    /// Keeps backups of its own naming spread over a month (T134; user chose "Spread over a month"):
    /// the newest 3; then the newest of each of the 4 latest days with none kept; then the newest of
    /// each of the 3 latest weeks with none kept. One whose time can't be read, and anything else in
    /// the folder, is left alone.
    /// </summary>
    public static void Prune(string folder)
    {
        if (!Directory.Exists(folder)) return;
        var backups = Dumps(folder)
            .Select(path => (Path: path, At: TakenAt(Path.GetFileNameWithoutExtension(path))))
            .Where(b => b.At is not null).Select(b => (b.Path, At: b.At!.Value))
            .OrderByDescending(b => b.At).ToList();
        var kept = backups.Take(Newest).ToList();
        KeepOnePer(b => b.At.Date, Days);
        KeepOnePer(b => (ISOWeek.GetYear(b.At), ISOWeek.GetWeekOfYear(b.At)), Weeks);

        foreach (var (old, _) in backups.Except(kept))
        {
            File.Delete(old);
            var manifest = Path.ChangeExtension(old, ".json");
            if (File.Exists(manifest)) File.Delete(manifest);
        }

        // The newest of each of the latest periods that have none kept yet, up to this many.
        void KeepOnePer<TPeriod>(Func<(string Path, DateTime At), TPeriod> period, int most)
        {
            var have = kept.Select(period).ToHashSet();
            kept.AddRange(backups.Except(kept).Where(b => !have.Contains(period(b))).GroupBy(period).Select(g => g.First()).Take(most));
        }
    }

    /// <summary>The backups in the folder whose manifest reads, newest first, <see cref="Kept"/> at most (T132).</summary>
    public static IReadOnlyList<UserDataBackup> List(string folder) =>
        [.. Readable(folder).OrderByDescending(b => b.Manifest.TakenAt).Take(Kept)];

    /// <summary>The backup of this name among the folder's own, with its manifest beside it; none if it isn't one (T132).</summary>
    public static string? Find(string folder, string name) =>
        Readable(folder).FirstOrDefault(b => string.Equals(Path.GetFileName(b.DumpPath), name, StringComparison.OrdinalIgnoreCase))?.DumpPath;

    // The folder's backups of its own naming whose manifest reads.
    private static IEnumerable<UserDataBackup> Readable(string folder)
    {
        if (!Directory.Exists(folder)) yield break;
        foreach (var dump in Dumps(folder))
        {
            BackupManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(Path.ChangeExtension(dump, ".json")), Json);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                continue;
            }
            if (manifest is { Rows: not null }) yield return new UserDataBackup(dump, manifest);
        }
    }

    // When a backup was taken, from its name (local time, to the minute); none if it can't be read.
    private static DateTime? TakenAt(string? name) =>
        (name?.StartsWith(Prefix, StringComparison.Ordinal) == true ? name[Prefix.Length..] : name?.StartsWith(FormerPrefix, StringComparison.Ordinal) == true ? name[FormerPrefix.Length..] : null) is { Length: >= 15 } stamp
        && DateTime.TryParseExact(stamp[..15], "yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)
            ? at : null;

    /// <summary>Whether a backup is due: none yet, or the newest a day old or more.</summary>
    public static bool IsDue(string folder, DateTimeOffset now)
    {
        if (!Directory.Exists(folder)) return true;
        var newest = Dumps(folder).Select(f => TakenAt(Path.GetFileNameWithoutExtension(f))).Max();
        return newest is null || now - new DateTimeOffset(newest.Value) >= TimeSpan.FromDays(1);
    }

    /// <summary>
    /// A backup taken by itself (after an import, a rescoring, a rebuild, or the app's start), into
    /// <see cref="FolderOf"/> the folder given (none: no backup): kept, pruned, and said; one that
    /// fails says why and stops nothing.
    /// </summary>
    public static async Task<UserDataBackup?> AutomaticallyAsync(string connectionString, string? folder, Action<string> say, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(say);
        folder = FolderOf(folder, connectionString);
        if (folder is null) return null;
        try
        {
            var backup = await BackupAsync(connectionString, folder, cancellationToken);
            Prune(folder);
            say($"Backed up to {backup.DumpPath}");
            return backup;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or NpgsqlException)
        {
            say($"Backup failed: {e.Message}");
            return null;
        }
    }

    // The dump's table of contents, its tables' rows reordered so a table comes after every table
    // it refers to (a table referring to itself, or a loop, keeps its place).
    private static async Task<string> ParentsFirstAsync(string connectionString, string contents, CancellationToken cancellationToken)
    {
        var parents = new Dictionary<string, HashSet<string>>();
        await using (var dataSource = Database.CreateDataSource(connectionString))
        await using (var query = dataSource.CreateCommand("""
            select c.relname, p.relname from pg_constraint k
            join pg_class c on c.oid = k.conrelid join pg_class p on p.oid = k.confrelid
            join pg_namespace n on n.oid = c.relnamespace
            where k.contype = 'f' and n.nspname = 'app' and c.oid <> p.oid
            """))
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                (parents.TryGetValue(reader.GetString(0), out var of) ? of : parents[reader.GetString(0)] = []).Add(reader.GetString(1));

        var depth = new Dictionary<string, int>();
        int Depth(string table, int guard) =>
            depth.TryGetValue(table, out var d) ? d
            : guard > parents.Count || !parents.TryGetValue(table, out var of) ? 0
            : depth[table] = 1 + of.Max(p => Depth(p, guard + 1));

        // A backup taken before the jobs were left out still holds theirs: not put back (T129); nor
        // is their numbering, or the next job's would clash with one since (T132).
        var lines = contents.Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => !NotTheUsers.Any(t => l.Contains($" TABLE DATA app {t} ", StringComparison.Ordinal) || l.Contains($" SEQUENCE SET app {t}_", StringComparison.Ordinal))).ToList();
        var rows = lines.Where(l => l.Contains(" TABLE DATA app ", StringComparison.Ordinal)).ToList();
        return string.Join('\n', lines.Where(l => !rows.Contains(l)).Concat(rows.OrderBy(l => Depth(l.Split(" TABLE DATA app ")[1].Split(' ')[0], 0))));
    }

    // "0040_saved_route_scores.sql" from its resource name.
    private static string Short(string migration) => migration.Replace("CutThrough.Data.Migrations.", "", StringComparison.Ordinal);

    private static async Task<string> LastAppliedAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var dataSource = Database.CreateDataSource(connectionString);
        await using var query = dataSource.CreateCommand("select scriptname from public.schemaversions where scriptname like '%.Migrations.%' order by scriptname desc limit 1");
        return (string?)await query.ExecuteScalarAsync(cancellationToken) ?? "";
    }

    // The app's tables that hold no user data (T129): the record of the jobs run, as they were run, and
    // the map services' API keys (T169), which stay on this computer. Neither backed up nor emptied by a
    // restore, which writes its own row in the first.
    private static readonly string[] NotTheUsers = ["map_updates", "api_keys"];

    internal static async Task<List<string>> TablesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        await using var list = dataSource.CreateCommand(
            "select table_name from information_schema.tables where table_schema = 'app' and table_type = 'BASE TABLE' and table_name <> all(@not) order by 1");
        list.Parameters.Add(new NpgsqlParameter("not", NotTheUsers));
        await using var reader = await list.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<Dictionary<string, long>> RowsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var dataSource = Database.CreateDataSource(connectionString);
        var rows = new Dictionary<string, long>();
        foreach (var table in await TablesAsync(dataSource, cancellationToken))
        {
            await using var count = dataSource.CreateCommand($"select count(*) from app.{table}");
            rows[table] = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }
        return rows;
    }

    // pg_dump or pg_restore, from PostgreSQL's own bin folder, against the connection string's
    // database (none: it reads only the file); what it writes out.
    private static async Task<string> RunAsync(string tool, string? connectionString, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(PgTool(tool)) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (connectionString is not null)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            start.ArgumentList.Add($"--dbname={builder.Database}");
            start.Environment["PGHOST"] = builder.Host;
            start.Environment["PGPORT"] = builder.Port.ToString(CultureInfo.InvariantCulture);
            start.Environment["PGUSER"] = builder.Username;
            if (!string.IsNullOrEmpty(builder.Password)) start.Environment["PGPASSWORD"] = builder.Password;
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{tool} didn't start.");
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException($"{tool} failed: {(await errors).Trim()}");
        return output;
    }

    // WEND_PG_BIN, or the newest PostgreSQL's bin under Program Files.
    private static string PgTool(string tool)
    {
        var exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        if (Environment.GetEnvironmentVariable("WEND_PG_BIN") is { Length: > 0 } bin) return Path.Combine(bin, exe);
        var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL");
        var found = Directory.Exists(programs)
            ? Directory.EnumerateDirectories(programs).Select(d => Path.Combine(d, "bin", exe)).Where(File.Exists)
                .OrderByDescending(p => int.TryParse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(p))), out var v) ? v : 0).FirstOrDefault()
            : null;
        return found ?? exe;
    }
}
