using System.Globalization;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;

namespace WendYourWay.Cli;

/// <summary>
/// `wend outings import-strava &lt;export.zip&gt;`: import the user's history from Strava's bulk
/// export, read in place, and sum up what happened. Never prints a position: the tracks start at
/// home.
/// </summary>
public static class StravaImportCommand
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    // The first few activities that couldn't be imported are listed; the rest are counted.
    private const int ListedNotRead = 10;

    public static async Task<int> Run(string archive, string connectionString, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!File.Exists(archive))
        {
            await output.WriteLineAsync($"No Strava archive at {Path.GetFullPath(archive)}. Download it from Strava's account settings.");
            return 1;
        }

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await output.WriteLineAsync($"Importing Strava archive {Path.GetFileName(archive)} → {database}");
        try
        {
            Migrations.Apply(connectionString);
            await using var dataSource = Database.CreateDataSource(connectionString);
            var import = await StravaImporter.ImportAsync(dataSource, archive, new Progress(output), cancellationToken);

            await output.WriteLineAsync();
            await Row(output, "imported", import.Imported,
                import.Imported == 0 ? "" : "(" + string.Join(", ", import.ImportedByKind.OrderBy(k => KindOrder(k.Key)).Select(k => $"{Kind(k.Key)} {k.Value}")) + ")");
            await Row(output, "already there", import.AlreadyThere, "");
            await Row(output, "deleted by you", import.Deleted, "not imported again");
            await Row(output, "skipped", import.SkippedByType.Values.Sum(),
                import.SkippedByType.Count == 0 ? "" : "no way on the ground: " + string.Join(", ", import.SkippedByType.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key} {s.Value}")));
            await Row(output, "outside E&W", import.OutsideEnglandAndWales, "never in England or Wales");
            await Row(output, "not imported", import.NotRead.Count, "");
            foreach (var (activity, why) in import.NotRead.Take(ListedNotRead))
                await output.WriteLineAsync($"      {activity}: {why}");
            if (import.NotRead.Count > ListedNotRead)
                await output.WriteLineAsync($"      … and {import.NotRead.Count - ListedNotRead} more");
            return 0;
        }
        catch (InvalidDataException e)
        {
            await output.WriteLineAsync($"Can't import it: {e.Message}.");
            return 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("Stopped. What was imported is kept: run it again to finish.");
            return 1;
        }
        catch (Exception e) when (e is InvalidOperationException or NpgsqlException)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync($"Import failed: {e.Message}");
            return 1;
        }
    }

    private static Task Row(TextWriter output, string label, int count, string detail) =>
        output.WriteLineAsync(string.Format(Culture, "  {0,-15}{1,6:N0}  {2}", label, count, detail).TrimEnd());

    private static int KindOrder(string kind) => Array.IndexOf(["run", "walk", "hike", "ride", "gravel_ride"], kind);

    private static string Kind(string kind) => kind.Replace('_', ' ');

    // Progress lines as the importer reports them, straight to the console, each with how long
    // the import has taken so far: a slow one then shows where the time went.
    private sealed class Progress(TextWriter output) : IProgress<string>
    {
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        public void Report(string value) =>
            output.WriteLine(string.Format(Culture, @"  {0,-48}{1:h\:mm\:ss}", value, _clock.Elapsed));
    }
}
