using WendYourWay.Data;
using WendYourWay.Data.Building;
using Npgsql;

namespace WendYourWay.Cli;

/// <summary>A job's own run (T129): its stage told as it goes, written to app.map_updates.</summary>
public sealed class JobRun(Jobs jobs, int id, NpgsqlDataSource dataSource)
{
    public int Id => id;

    public NpgsqlDataSource DataSource => dataSource;

    public Task StageAsync(string stage, CancellationToken cancellationToken = default) => jobs.StageAsync(id, stage, cancellationToken: cancellationToken);
}

/// <summary>How a job ended: its outcome (finished, up to date or failed), a sentence why, and its exit code.</summary>
public sealed record JobEnd(string Outcome, string Message, int ExitCode)
{
    public static JobEnd Finished(string message) => new("finished", message, 0);

    public static JobEnd Failed(string message) => new("failed", message, 1);
}

/// <summary>
/// Runs a job as every job started from the app is run (T129): migrations applied, the one lock
/// taken (refused if another job or a build holds it), its row in app.map_updates, a heartbeat each
/// minute while it runs, and how it ended, with a sentence why, written and said. A failure it
/// doesn't foresee ends it as failed rather than leaving it running.
/// </summary>
public static class JobRuns
{
    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromMinutes(1);

    public static async Task<int> RunAsync(
        string connectionString, string kind, string source, TextWriter output, Func<JobRun, Task<JobEnd>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            Migrations.Apply(connectionString);
        }
        catch (Exception e) when (e is InvalidOperationException or NpgsqlException)
        {
            await output.WriteLineAsync($"Failed: {e.Message}");
            return 1;
        }

        await using var held = await UpdateLock.TryTakeAsync(connectionString, CancellationToken.None);
        if (held is null)
        {
            await output.WriteLineAsync(UpdateLock.Held);
            return 1;
        }

        // Awake till it ends (T138): a laptop asleep stops a job part-way.
        using var awake = new StayingAwake();
        await using var dataSource = Database.CreateDataSource(connectionString);
        var jobs = new Jobs(dataSource);
        var id = await jobs.StartAsync(kind, source, cancellationToken);
        using var beating = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = BeatAsync(jobs, id, beating.Token);
        JobEnd end;
        try
        {
            end = await work(new JobRun(jobs, id, dataSource));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            end = JobEnd.Failed("Stopped. What was done is kept; running it again finishes it.");
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or InvalidDataException or NpgsqlException or UnauthorizedAccessException)
        {
            end = JobEnd.Failed($"It failed: {e.Message}");
        }
        finally
        {
            await beating.CancelAsync();
            await heartbeat;
        }
        await jobs.EndAsync(id, end.Outcome, end.Message, CancellationToken.None);
        await output.WriteLineAsync(end.Message);
        return end.ExitCode;
    }

    private static async Task BeatAsync(Jobs jobs, int id, CancellationToken stop)
    {
        using var every = new PeriodicTimer(HeartbeatEvery);
        try
        {
            while (await every.WaitForNextTickAsync(stop)) await jobs.HeartbeatAsync(id, stop);
        }
        catch (OperationCanceledException)
        {
            // Finished.
        }
        catch (NpgsqlException)
        {
            // Unsaid this time; the job goes on.
        }
    }
}
