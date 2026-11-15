using System.Globalization;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;

namespace WendYourWay.Cli;

/// <summary>
/// `wend outings score [--all]` (PLAN T097, STORY-045): the app's score for each Outing not scored
/// yet, from which its stars are drawn, or for every one again after a scoring rule changes. Each
/// Outing is kept as it's scored, so stopped it can be run again to finish. Prints counts only.
/// </summary>
public static class OutingsScoreCommand
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>What to run to finish the scores, when a pass is stopped.</summary>
    public const string Finish = "Run wend outings score to finish them.";

    public static async Task<int> Run(string connectionString, bool all, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        await output.WriteLineAsync($"Scoring {(all ? "every Outing again" : "the Outings not scored yet")} → {new NpgsqlConnectionStringBuilder(connectionString).Database}");
        try
        {
            Migrations.Apply(connectionString);
            await using var dataSource = Database.CreateDataSource(connectionString);
            var scores = await OutingScoring.ScoreAsync(dataSource, all, progress: Progress(output), cancellationToken: cancellationToken);
            // T115: Saved Routes saved before the stars, scored as Outings are.
            var savedRoutes = await OutingScoring.ScoreSavedRoutesAsync(dataSource, cancellationToken);
            await output.WriteLineAsync();
            await Report(output, scores);
            await output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}", "Saved Routes scored", savedRoutes));
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync($"Stopped. What was scored is kept. {Finish}");
            return 1;
        }
        catch (Exception e) when (e is InvalidOperationException or NpgsqlException)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync($"Scoring failed: {e.Message}");
            return 1;
        }
    }

    /// <summary>Every hundred Outings scored, how far it has got.</summary>
    internal static Action<int, int> Progress(TextWriter output) => (done, of) =>
    {
        if (done % 100 == 0) output.WriteLine(string.Format(Culture, "  scored {0:N0} of {1:N0}", done, of));
    };

    internal static async Task Report(TextWriter output, OutingScores scores)
    {
        await output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}", "Outings scored", scores.Scored));
        await output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}", "  of them, off the Network: can't be rated", scores.Unrated));
    }
}
