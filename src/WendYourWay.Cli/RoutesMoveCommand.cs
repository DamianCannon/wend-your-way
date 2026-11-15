using System.Globalization;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Cli;

/// <summary>
/// `wend routes move` (PLAN T136, STORY-016): every Saved Route the map has changed under moved onto
/// the map as it is, if it would follow the same line; the rest kept as they were, each said with
/// where it no longer follows the map. `wend update` does this after it builds. Seconds of work, so
/// not a job: it takes no lock.
/// </summary>
public static class RoutesMoveCommand
{
    public static async Task<int> Run(string connectionString, string? terrain, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        try
        {
            Migrations.Apply(connectionString);
            await using var dataSource = Database.CreateDataSource(connectionString);
            await MoveAsync(dataSource, terrain, output, cancellationToken);
            return 0;
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Failed: {e.Message}");
            return 1;
        }
    }

    /// <summary>Moves those the map has changed under, saying each, then how many moved and were kept.</summary>
    internal static async Task MoveAsync(NpgsqlDataSource dataSource, string? terrain, TextWriter output, CancellationToken cancellationToken)
    {
        var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), ScoringWeights.Default,
            terrain is null ? null : new TerrainGrid(terrain), new RouteMatching(dataSource));
        var lines = new List<string>();
        var (moved, kept) = await new SavedRouteMoves(new PostgisSavedRoutes(dataSource), planner, dataSource).MoveChangedAsync(
            (name, move) => lines.Add(move.Outcome == RouteMoveOutcome.Moved ? $"  {name}: moved onto the map as it is" : $"  {name}: kept as it was. {move.Said}"),
            cancellationToken);
        foreach (var line in lines) await output.WriteLineAsync(line);
        await output.WriteLineAsync(moved + kept == 0 ? "No Saved Route has had the map change under it."
            : string.Format(CultureInfo.InvariantCulture, "Saved Routes the map changed under: {0} moved onto it, {1} kept as {2}.", moved, kept, kept == 1 ? "it was" : "they were"));
    }
}
