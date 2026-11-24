using System.Text.Json;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Outings;
using WendYourWay.Data.UserData;
using Npgsql;
using WendYourWay.Data.Places;

namespace WendYourWay.Web.Planning;

/// <summary>
/// Wend Your Way at a glance, for the Admin panel (T129): the user's Outings, how many they haven't
/// rated and when they last came in; the last backup and where; the room on the data's drive; and
/// the job running, if one is; the last import of runs (T130), however it went; and the backups there
/// and the last restore (T132); and the map before, to go back to, and how the last change of map went (T131).
/// </summary>
public sealed record AdminStatus(
    int Outings, int Unrated, DateTimeOffset? LastImported, DateTimeOffset? LastBackup, string? BackupFolder,
    double? FreeGb, string? Drive, Job? Running, Job? LastImport = null, IReadOnlyList<BackupListed>? Backups = null, Job? LastRestore = null,
    MapBefore? MapBefore = null, Job? LastBack = null, OsDataInUse? OsData = null)
{
    /// <summary>Before anything's known.</summary>
    public static AdminStatus Unknown { get; } = new(0, 0, null, null, null, null, null, null);

    /// <summary>Whether there's room for a map update (`wend update` refuses under this).</summary>
    public bool RoomForAnUpdate => FreeGb is not { } free || free >= UpdateRoom.LeastGb;
}

/// <summary>
/// OS's open data in use (T171, #175): the release of OS Open Names loaded, and of OS Terrain 50 unpacked, each
/// null when it isn't known (loaded by hand, before T171); and whether there are heights at all.
/// </summary>
public sealed record OsDataInUse(string? NamesVersion, string? HeightsVersion, bool HasHeights);

/// <summary>A job under way (T138): it, and how long the last of its kind that ended well took, if one has.</summary>
public sealed record UnderWay(Job Job, TimeSpan? TookLast);

/// <summary>
/// The app's database changes held back as it started, for a job running (#153, T149): whether it still is,
/// or it's done and the app needs opening again to make them.
/// </summary>
public sealed record HeldBack(bool StillRunning);

/// <summary>The migrations held back as the app started (#153, T149), set once it's readied its database.</summary>
public sealed class HeldBackAtStart
{
    public IReadOnlyList<string> Migrations { get; set; } = [];
}

/// <summary>A backup in the folder (T132): its file's name, when it was taken, and how many Outings it holds.</summary>
public sealed record BackupListed(string Name, DateTimeOffset TakenAt, long Outings);

/// <summary>The room a map update needs, as `wend update` asks it (T129).</summary>
public static class UpdateRoom
{
    /// <summary>Kept the same as the CLI's: 40 GB on the data's drive.</summary>
    public const double LeastGb = 40;
}

/// <summary>Wend Your Way at a glance (T129), its jobs started (T130), and backups taken (T132).</summary>
public interface IAdmin
{
    Task<AdminStatus> StatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a job of this kind (<see cref="JobKind"/>), a restore of the backup of this name; why not, if it can't be.</summary>
    Task<string?> StartAsync(string kind, string? backup = null, CancellationToken cancellationToken = default);

    /// <summary>The job under way, if one is (T138): any kind, not stopped.</summary>
    Task<UnderWay?> UnderWayAsync(CancellationToken cancellationToken = default);

    /// <summary>The database changes held back as the app started, if any were (#153, T149).</summary>
    Task<HeldBack?> HeldBackAsync(CancellationToken cancellationToken = default);

    /// <summary>Backs up now, in the app: no lock, no job (T132). Where it went, or why it failed.</summary>
    Task<string> BackUpAsync(CancellationToken cancellationToken = default);
}

/// <summary>The Admin panel's status, read from the database, the backup folder and the disk; its jobs started with wend.</summary>
/// <param name="backupFolder">This database's own backup folder; none for a test database.</param>
/// <param name="importFolder">Where runs are imported from (`Import:Folder`); the user's Downloads, as wend finds it, by default.</param>
public sealed class AdminView(
    NpgsqlDataSource dataSource, string connectionString, string? backupFolder, string dataFolder, TimeProvider time, WendLauncher launcher, string? importFolder = null, WorkUnderWay? underWay = null, HeldBackAtStart? heldBack = null, string? terrainFolder = null) : IAdmin
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AdminStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        var counts = await OutingCounts.CountAsync(dataSource, cancellationToken);
        var jobs = new Jobs(dataSource);
        var last = await jobs.LastAsync(cancellationToken: cancellationToken);
        var running = last is { Outcome: "running" } job && !job.Stopped(time.GetUtcNow()) ? job : null;
        var (freeGb, drive) = Room();
        var backups = backupFolder is null ? [] : UserDataBackups.List(backupFolder)
            .Select(b => new BackupListed(Path.GetFileName(b.DumpPath), b.Manifest.TakenAt, b.Manifest.Rows.GetValueOrDefault("outings"))).ToList();
        return new AdminStatus(counts.Outings, counts.Unrated, counts.LastImported, LastBackup(), backupFolder, freeGb, drive, running,
            await jobs.LastAsync(JobKind.Import, cancellationToken), backups, await jobs.LastAsync(JobKind.Restore, cancellationToken),
            await NetworkBefore.FindAsync(dataSource, cancellationToken), await jobs.LastAsync(JobKind.Back, cancellationToken),
            await OsDataAsync(cancellationToken));
    }

    // Which release of OS's place names and heights is in use (T171): the places' own record, and the terrain folder's.
    private async Task<OsDataInUse> OsDataAsync(CancellationToken cancellationToken)
    {
        string? names;
        try
        {
            names = (await PlacesImport.ReleaseAsync(dataSource, cancellationToken))?.Release.Version;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Its migration held back while a job runs (T149): not known yet.
            names = null;
        }
        var hasHeights = terrainFolder is not null && Directory.Exists(terrainFolder);
        return new OsDataInUse(names, hasHeights ? TerrainRelease.In(terrainFolder!)?.Version : null, hasHeights);
    }

    public async Task<UnderWay?> UnderWayAsync(CancellationToken cancellationToken = default)
    {
        var jobs = new Jobs(dataSource);
        if (await jobs.LastAsync(cancellationToken: cancellationToken) is not { Outcome: "running" } job || job.Stopped(time.GetUtcNow())) return null;
        var last = await jobs.LastEndedWellAsync(job.Kind, cancellationToken);
        return new UnderWay(job, last is { FinishedAt: { } finished } ? finished - last.StartedAt : null);
    }

    public async Task<HeldBack?> HeldBackAsync(CancellationToken cancellationToken = default) =>
        heldBack is not { Migrations.Count: > 0 } ? null : new HeldBack(await UpdateLock.IsHeldAsync(connectionString, cancellationToken));

    public async Task<string?> StartAsync(string kind, string? backup = null, CancellationToken cancellationToken = default)
    {
        if ((await StatusAsync(cancellationToken)).Running is not null) return "Something's already running: try again when it's done.";
        return kind switch
        {
            JobKind.Back => launcher.Start("back", ["network", "back"]),
            JobKind.Import => launcher.Start("import", importFolder is null ? ["outings", "import-downloads"] : ["outings", "import-downloads", "--folder", importFolder]),
            // Only a backup among the folder's own, found by its name: never a path from the page.
            JobKind.Restore => backupFolder is not null && backup is not null && UserDataBackups.Find(backupFolder, backup) is { } dump
                ? launcher.Start("restore", ["restore", dump])
                : "That backup isn't in the folder any more.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a job the Admin panel starts."),
        };
    }

    public async Task<string> BackUpAsync(CancellationToken cancellationToken = default)
    {
        if (backupFolder is null) return "There's no backup folder for this database.";
        try
        {
            // Not stopped with the page, and finished before the app stops (T138).
            var folder = backupFolder;
            var backup = await (underWay ?? new WorkUnderWay()).Run(async () =>
            {
                var taken = await UserDataBackups.BackupAsync(connectionString, folder, CancellationToken.None);
                UserDataBackups.Prune(folder);
                return taken;
            });
            return $"Backed up to {backup.DumpPath}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or NpgsqlException)
        {
            return $"The backup failed: {e.Message}";
        }
    }

    // The newest backup's own record of when it was taken; none if there's no folder or no backup.
    // The newest backup's own record of when it was taken, of either naming (T140); none if there's no folder or no backup.
    private DateTimeOffset? LastBackup() => backupFolder is null ? null : UserDataBackups.List(backupFolder) is [var newest, ..] ? newest.Manifest.TakenAt : null;

    private (double? FreeGb, string? Drive) Room()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dataFolder))!);
            return (drive.AvailableFreeSpace / 1e9, drive.Name);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }
}
