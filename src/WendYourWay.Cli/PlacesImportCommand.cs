using System.Diagnostics;
using System.Globalization;
using WendYourWay.Data;
using WendYourWay.Data.Places;
using Npgsql;

namespace WendYourWay.Cli;

/// <summary>
/// `wend places import &lt;folder&gt;` (PLAN T064): load OS Open Names' places and postcodes for England
/// and Wales, read in place from the user's download, with the map's own named green areas, in
/// place of any loaded before. Prints counts only.
/// </summary>
public static class PlacesImportCommand
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    // How often it says how far it has read.
    private const int SayEvery = 100;

    public static async Task<int> Run(string folder, string connectionString, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var files = OpenNames.Files(folder);
        if (files.Count == 0)
        {
            await output.WriteLineAsync($"No OS Open Names CSV files at {Path.GetFullPath(folder)}.");
            return 1;
        }

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await output.WriteLineAsync($"Loading places from {files.Count} OS Open Names {(files.Count == 1 ? "file" : "files")} → {database}");
        try
        {
            Migrations.Apply(connectionString);
            await using var dataSource = Database.CreateDataSource(connectionString);
            var clock = Stopwatch.StartNew();
            var read = 0;
            var loaded = await PlacesImport.LoadAsync(dataSource, files, _ =>
            {
                if (++read % SayEvery == 0) output.WriteLine(string.Format(Culture, "  read {0} of {1} files", read, files.Count));
            }, cancellationToken);

            await output.WriteLineAsync();
            await output.WriteLineAsync(string.Format(Culture, "  {0,-26}{1,12:N0}", "Places", loaded.Places));
            await output.WriteLineAsync(string.Format(Culture, "  {0,-26}{1,12:N0}", "Postcodes", loaded.Postcodes));
            await output.WriteLineAsync(string.Format(Culture, "  {0,-26}{1,12:N0}", "Postcode districts", loaded.Districts));
            await output.WriteLineAsync(string.Format(Culture, "  {0,-26}{1,12:N0}", "Green areas from the map", loaded.GreenAreas));
            await output.WriteLineAsync(string.Format(Culture, "  took {0:0.0} s", clock.Elapsed.TotalSeconds));
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("Stopped. The places loaded before are kept.");
            return 1;
        }
    }
}
