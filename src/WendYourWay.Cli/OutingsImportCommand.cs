using System.Globalization;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;

namespace WendYourWay.Cli;

/// <summary>
/// `wend outings import-gpx &lt;file-or-folder&gt;`: import the user's own runs and rides from GPX
/// files, and report what each covered and where it left the Network. Never prints a position:
/// the tracks start at home.
/// </summary>
public static class OutingsImportCommand
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static async Task<int> Run(string fileOrFolder, string connectionString, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        string[] files = Directory.Exists(fileOrFolder)
            ? [.. Directory.EnumerateFiles(fileOrFolder).Where(IsTrack).Order(StringComparer.OrdinalIgnoreCase)]
            : File.Exists(fileOrFolder) ? [fileOrFolder] : [];
        if (files.Length == 0)
        {
            await output.WriteLineAsync($"No GPX, TCX or FIT files at {Path.GetFullPath(fileOrFolder)}.");
            return 1;
        }

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await output.WriteLineAsync($"Importing {files.Length} {(files.Length == 1 ? "file" : "files")} → {database}");
        try
        {
            Migrations.Apply(connectionString);
            await using var dataSource = Database.CreateDataSource(connectionString);

            int imported = 0, already = 0, skipped = 0;
            foreach (var file in files)
            {
                try
                {
                    var outing = await OutingImporter.ImportGpxAsync(dataSource, file, cancellationToken);
                    if (outing.AlreadyImported) already++;
                    else imported++;
                    await output.WriteLineAsync(Describe(outing));
                }
                catch (InvalidDataException e)
                {
                    skipped++;
                    await output.WriteLineAsync(string.Format(Culture, "  {0,-28}skipped: {1}", Path.GetFileName(file), e.Message));
                }
            }

            await output.WriteLineAsync();
            await output.WriteLineAsync($"  {imported} imported, {already} already there, {skipped} skipped");
            return 0;
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

    private static string Describe(OutingImport outing)
    {
        var gaps = outing.GapsM.Count switch
        {
            0 => "no gaps",
            1 => string.Format(Culture, "1 gap: {0:0} m", outing.GapsM[0]),
            _ => string.Format(Culture, "{0} gaps: {1}", outing.GapsM.Count, string.Join(", ", outing.GapsM.Select(g => g.ToString("0", Culture) + " m"))),
        };
        return string.Format(Culture, "  {0,-28}{1,-12}{2,8:0.00} km{3,6:0}% covered{4,6} Segments   {5}{6}",
            Truncate(outing.Name, 27),
            outing.StartedAt?.UtcDateTime.ToString("yyyy-MM-dd", Culture) ?? "",
            outing.LengthM / 1000,
            outing.Coverage * 100,
            outing.SegmentsCovered,
            gaps,
            outing.AlreadyImported ? "   (already imported)" : "");
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    // The formats RecordedTrack reads: GPX, TCX and FIT, each perhaps gzipped as Strava keeps them.
    private static bool IsTrack(string path)
    {
        var name = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? path[..^3] : path;
        return Path.GetExtension(name).ToUpperInvariant() is ".GPX" or ".TCX" or ".FIT";
    }
}
