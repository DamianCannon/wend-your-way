using System.Globalization;
using WendYourWay.Data.Building;

namespace WendYourWay.Cli;

/// <summary>
/// `wend network back` (#135, T131, STORY-048): back to the map before, if a new one turns out bad;
/// done again, forward again. Run as a job (T129): the one lock, its row, `--log`. The swap and
/// the Outings' coverage in one transaction; then it finishes as an update does (Saved Routes
/// moved onto it, scores, the stars calibrated, a backup). One that stopped after its swap is
/// finished by the next, not swapped again.
/// </summary>
public static class NetworkBackCommand
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>The stages after the swap: a run stopped at one of them has swapped its map in.</summary>
    private static readonly string[] AfterTheSwap = ["swapped", "moving Saved Routes", "calibrating", "backing up"];

    public static Task<int> Run(string connectionString, string? terrain, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return JobRuns.RunAsync(connectionString, JobKind.Back, "network_prev", output, run => BackAsync(run, connectionString, terrain, output, cancellationToken), cancellationToken);
    }

    private static async Task<JobEnd> BackAsync(JobRun run, string connectionString, string? terrain, TextWriter output, CancellationToken cancellationToken)
    {
        var jobs = new Jobs(run.DataSource);
        if (await jobs.LastBeforeAsync(run.Id, [JobKind.Update, JobKind.Back], cancellationToken) is { Kind: JobKind.Back, Outcome: not "finished" } stopped
            && AfterTheSwap.Contains(stopped.Stage))
        {
            await output.WriteLineAsync($"Going to another map stopped after its swap, {stopped.StartedAt.ToLocalTime().ToString("d MMM HH:mm", Culture)}: finishing it.");
            await UpdateCommand.FinishAsync(stage => run.StageAsync(stage, cancellationToken), run.DataSource, connectionString, terrain, output, cancellationToken);
            await jobs.EndAsync(stopped.Id, "failed", "Stopped after its swap; finished by the one after it.", cancellationToken);
            return JobEnd.Finished("Finished going to the map that was swapped in.");
        }

        await run.StageAsync("swapping", cancellationToken);
        MapBefore to;
        try
        {
            to = await NetworkBefore.SwapAsync(run.DataSource, cancellationToken);
        }
        catch (NoMapBeforeException e)
        {
            return JobEnd.Failed(e.Message);
        }
        await run.StageAsync("swapped", cancellationToken);
        var said = Said(to);
        await output.WriteLineAsync($"  {said}");
        await UpdateCommand.FinishAsync(stage => run.StageAsync(stage, cancellationToken), run.DataSource, connectionString, terrain, output, cancellationToken);
        return JobEnd.Finished(said);
    }

    // "Gone back to the map built 16 Nov 2026." (T150), "… the map of 1 Oct 2026." for one with no build date kept, "Gone forward to …", or "… the map before this one." with no date.
    private static string Said(MapBefore to) =>
        $"Gone {(to.Newer ? "forward" : "back")} to the map {(to.BuiltAt is { } built ? "built " + built.ToLocalTime().ToString("d MMM yyyy", Culture) : to.At is { } at ? "of " + at.UtcDateTime.ToString("d MMM yyyy", Culture) : "before this one")}.";
}
