using System.Globalization;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using Npgsql;
using WendYourWay.Data.UserData;

namespace WendYourWay.Cli;

/// <summary>
/// `wend backup [folder]` and `wend restore &lt;file&gt;` (PLAN T102, STORY-031): the user's own data
/// (Outings, stars, Saved Routes, Marks, settings) to a dated file and back. Also taken by itself
/// after each import, scoring and build. The folder: the one given, else WEND_BACKUP_FOLDER,
/// else Wend Your Way Backups in OneDrive. Prints counts and paths only.
/// </summary>
public static class BackupCommand
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>The usual folder for this database's backups; none for a test database.</summary>
    internal static string? UsualFolder(string connectionString) =>
        UserDataBackups.FolderOf(UserDataBackups.Folder(Environment.GetEnvironmentVariable("WEND_BACKUP_FOLDER")), connectionString);

    public static async Task<int> Backup(string? folder, string connectionString, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        folder ??= UsualFolder(connectionString);
        if (folder is null)
        {
            await output.WriteLineAsync("No backup folder: give one, set WEND_BACKUP_FOLDER, or sign in to OneDrive.");
            return 1;
        }
        try
        {
            var backup = await UserDataBackups.BackupAsync(connectionString, folder, cancellationToken);
            UserDataBackups.Prune(folder);
            await output.WriteLineAsync($"Backed up {new NpgsqlConnectionStringBuilder(connectionString).Database} to {backup.DumpPath}");
            foreach (var (table, rows) in backup.Manifest.Rows.Where(r => r.Value > 0))
                await output.WriteLineAsync(string.Format(Culture, "  {0,-30}{1,12:N0}", table, rows));
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or NpgsqlException)
        {
            await output.WriteLineAsync($"Backup failed: {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// `wend restore`, run as a job (T132): the one lock, its row, how it went. What's there now is
    /// backed up first and not pruned (the very backup being read could go). A table whose rows
    /// aren't as the backup held them fails it, naming the table.
    /// </summary>
    public static Task<int> Restore(string dumpPath, string connectionString, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return JobRuns.RunAsync(connectionString, JobKind.Restore, dumpPath, output, run => RestoreAsync(run, dumpPath, connectionString, output, cancellationToken), cancellationToken);
    }

    private static async Task<JobEnd> RestoreAsync(JobRun run, string dumpPath, string connectionString, TextWriter output, CancellationToken cancellationToken)
    {
        if (!File.Exists(dumpPath) || !File.Exists(Path.ChangeExtension(dumpPath, ".json")))
            return JobEnd.Failed($"No backup there: {dumpPath} (and its .json beside it)");
        await run.StageAsync("restoring", cancellationToken);
        RestoreReport report;
        try
        {
            // What's there now is backed up first, to the usual folder; a test database's (or one
            // with no folder) to a temporary one, never among the real backups.
            var first = UsualFolder(connectionString) ?? Directory.CreateTempSubdirectory("cutthrough-before-restore-").FullName;
            report = await UserDataBackups.RestoreAsync(connectionString, dumpPath, first, cancellationToken);
        }
        catch (BackupTooNewException e)
        {
            return JobEnd.Failed(e.Message);
        }
        await output.WriteLineAsync($"What was there first: {report.Before.DumpPath}");
        await output.WriteLineAsync($"Restored {new NpgsqlConnectionStringBuilder(connectionString).Database} from {dumpPath}");
        await output.WriteLineAsync(string.Format(Culture, "  {0,-30}{1,12}{2,12}", "", "backed up", "restored"));
        foreach (var (table, (expected, restored)) in report.Rows.Where(r => r.Value.Expected > 0 || r.Value.Restored > 0))
            await output.WriteLineAsync(string.Format(Culture, "  {0,-30}{1,12:N0}{2,12:N0}{3}", table, expected, restored, expected == restored ? "" : "  differs"));

        var differs = report.Rows.Where(r => r.Value.Expected != r.Value.Restored).OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => string.Format(Culture, "{0} {1:N0} of {2:N0}", r.Key, r.Value.Restored, r.Value.Expected)).ToList();
        if (differs.Count > 0) return JobEnd.Failed($"Put back, but not as the backup held it: {string.Join(", ", differs)}.");
        var outings = report.Rows.TryGetValue("outings", out var o) ? o.Restored : 0;
        return JobEnd.Finished(string.Format(Culture, "Put back your data as it was on {0:d MMM yyyy, HH:mm}: {1:N0} {2}.",
            report.TakenAt.ToLocalTime(), outings, outings == 1 ? "Outing" : "Outings"));
    }

    /// <summary>After a command that changed the user's data has finished well: a backup, said in a line; a failure stops nothing.</summary>
    internal static async Task<int> Then(int exitCode, string connectionString, TextWriter output, CancellationToken cancellationToken)
    {
        if (exitCode == 0)
            await UserDataBackups.AutomaticallyAsync(connectionString, UserDataBackups.Folder(Environment.GetEnvironmentVariable("WEND_BACKUP_FOLDER")),
                line => output.WriteLine(line), cancellationToken);
        return exitCode;
    }
}
