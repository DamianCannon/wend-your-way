using System.Globalization;
using WendYourWay.Data.Building;
using WendYourWay.Data.Outings;

namespace WendYourWay.Cli;

/// <summary>
/// `wend outings import-downloads` (PLAN T130, STORY-048; user: "with gpx import you should skip files
/// that have already been imported as we don't want duplicates and yes being able to import loose
/// files is useful as I do use other tracking apps like Footpath"; chose "Downloads, recorded only"):
/// the newest Strava export in the folder, and every loose GPX, TCX or FIT there that's a recording.
/// A planned route (Wend Your Way's own exports among them), one already in, one the user deleted, or
/// one outside England and Wales is skipped, and each is counted apart. Run as a job (T129).
/// </summary>
public static class DownloadsImportCommand
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>The user's Downloads folder.</summary>
    public static string Downloads => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public static Task<int> Run(string folder, string connectionString, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return JobRuns.RunAsync(connectionString, JobKind.Import, folder, output, run => ImportAsync(run, folder, connectionString, output, cancellationToken), cancellationToken);
    }

    private static async Task<JobEnd> ImportAsync(JobRun run, string folder, string connectionString, TextWriter output, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder)) return JobEnd.Failed($"There's no folder at {folder} to import from.");
        await output.WriteLineAsync($"Importing runs from {folder}");
        var counts = new Counts();

        // The newest Strava export: one before it was read when it was the newest.
        if (Directory.EnumerateFiles(folder, "export_*.zip").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() is { } export)
        {
            await run.StageAsync("reading Strava's export", cancellationToken);
            await output.WriteLineAsync($"  {Path.GetFileName(export)}");
            try
            {
                var strava = await StravaImporter.ImportAsync(run.DataSource, export, cancellationToken: cancellationToken);
                (counts.FromStrava, counts.Already, counts.Deleted, counts.Outside, counts.Unreadable) =
                    (strava.Imported, strava.AlreadyThere, strava.Deleted, strava.OutsideEnglandAndWales, strava.NotRead.Count);
            }
            catch (InvalidDataException e)
            {
                await output.WriteLineAsync($"    not read: {e.Message}");
                counts.Unreadable++;
            }
        }

        var files = Directory.EnumerateFiles(folder).Where(IsTrack).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count > 0) await run.StageAsync($"reading {files.Count} {(files.Count == 1 ? "file" : "files")}", cancellationToken);
        foreach (var file in files)
        {
            string said;
            try
            {
                var outing = await OutingImporter.ImportGpxAsync(run.DataSource, file, cancellationToken);
                if (!outing.AlreadyImported) (counts.Loose, said) = (counts.Loose + 1, "imported");
                else if (outing.SameStartOnly) (counts.SameStart, said) = (counts.SameStart + 1, "already in: one starting within a minute of it");
                else (counts.Already, said) = (counts.Already + 1, "already in");
            }
            catch (InvalidDataException e)
            {
                said = OutingSkips.Of(e) switch
                {
                    OutingSkip.PlannedRoute => Count(ref counts.Planned, "a planned route, not a recording"),
                    OutingSkip.Deleted => Count(ref counts.Deleted, "deleted by you, not imported again"),
                    OutingSkip.OutsideEnglandAndWales => Count(ref counts.Outside, "outside England and Wales"),
                    _ => Unread(counts, file, e),
                };
            }
            await output.WriteLineAsync($"  {Path.GetFileName(file)}: {said}");
        }

        // Nothing new, nothing changed: no calibration and no backup (T134).
        if (counts.Added > 0)
        {
            await run.StageAsync("calibrating", cancellationToken);
            await StarCalibration.CalibrateAsync(run.DataSource, cancellationToken);
            await run.StageAsync("backing up", cancellationToken);
            await BackupCommand.Then(0, connectionString, output, cancellationToken);
        }
        return JobEnd.Finished(counts.Said());
    }

    // Not read: counted, and named, so the panel can say which.
    private static string Unread(Counts counts, string file, InvalidDataException e)
    {
        counts.Unreadable++;
        counts.UnreadFiles.Add(Path.GetFileName(file));
        return $"not read: {e.Message}";
    }

    private static string Count(ref int count, string said)
    {
        count++;
        return said;
    }

    // The formats RecordedTrack reads: GPX, TCX and FIT, each perhaps gzipped.
    private static bool IsTrack(string path)
    {
        var name = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? path[..^3] : path;
        return Path.GetExtension(name).ToUpperInvariant() is ".GPX" or ".TCX" or ".FIT";
    }

    private sealed class Counts
    {
        public int FromStrava;
        public int Loose;
        public int Already;
        public int SameStart;
        public int Planned;
        public int Deleted;
        public int Outside;
        public int Unreadable;
        public List<string> UnreadFiles { get; } = [];

        public int Added => FromStrava + Loose;

        /// <summary>What was done, in a sentence: what came in, then what didn't and why, each only if any.</summary>
        public string Said()
        {
            var came = Added == 0 ? "No new runs to import." : string.Format(Culture, "Imported {0:N0} new {1}{2}.", Added, Added == 1 ? "run" : "runs",
                FromStrava > 0 && Loose > 0 ? string.Format(Culture, " ({0:N0} from Strava's export, {1:N0} from files)", FromStrava, Loose) : "");
            var not = new[]
            {
                (Already, "already in"),
                (SameStart, "already in by their start"),
                (Planned, Planned == 1 ? "a planned route, not a recording" : "planned routes, not recordings"),
                (Deleted, "deleted by you"),
                (Outside, "outside England and Wales"),
                (Unreadable, "couldn't be read" + (UnreadFiles.Count > 0 ? ": " + string.Join(", ", UnreadFiles) : "")),
            }.Where(n => n.Item1 > 0).Select(n => string.Format(Culture, "{0:N0} {1}", n.Item1, n.Item2)).ToList();
            return not.Count == 0 ? came : came + " Skipped: " + string.Join(", ", not) + ".";
        }
    }
}
