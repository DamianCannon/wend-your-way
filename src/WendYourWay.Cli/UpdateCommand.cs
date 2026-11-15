using System.Globalization;
using System.Text;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Outings;
using Npgsql;

namespace WendYourWay.Cli;

/// <summary>
/// `wend update` (PLAN T127, STORY-047; user: "a single click way to update the application so that it
/// has the latest OSM maps ... robust so that failures in the middle don't break the application"):
/// the map brought up to date in one go. Each stage is written to app.map_updates as it's reached,
/// with a heartbeat while it runs, so the app can say how it's getting on or how it ended.
/// What a failure leaves: in the download, a `.part`, the extract and map untouched; in the import,
/// osm marked unfinished, the map untouched; in the build, nothing (its transaction); after the
/// swap, the run at `built`, which the next finishes without a download.
/// </summary>
public static class UpdateCommand
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The least room a Great Britain update needs on the data's drive (T129): its last build took
    /// the Network 13 GB, the one before it kept 13 GB, osm 6 GB; osm2pgsql's node file more while it
    /// runs. Under this, it doesn't start.
    /// </summary>
    public const double LeastRoomGb = 40;

    public static async Task<int> Run(
        string source, string dataFolder, string connectionString, string? osm2pgsqlExe, string? terrain, string rightsOfWay,
        TextWriter output, double leastRoomGb = LeastRoomGb, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        await output.WriteLineAsync($"Updating the map from {source} → {new NpgsqlConnectionStringBuilder(connectionString).Database} ({DateTimeOffset.Now.ToString("d MMM yyyy HH:mm", Culture)})");
        try
        {
            Migrations.Apply(connectionString);
        }
        catch (Exception e) when (e is InvalidOperationException or NpgsqlException)
        {
            await output.WriteLineAsync($"Update failed: {e.Message}");
            return 1;
        }

        await using var held = await UpdateLock.TryTakeAsync(connectionString, CancellationToken.None);
        if (held is null)
        {
            await output.WriteLineAsync(UpdateLock.Held);
            return 1;
        }

        // Awake till it ends (T138): a 3½-hour update stopped by sleep is finished only by the next.
        using var awake = new StayingAwake();
        await using var dataSource = Database.CreateDataSource(connectionString);
        var updates = new Jobs(dataSource);
        var id = await updates.StartAsync(JobKind.Update, source, cancellationToken);
        using var beating = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = BeatAsync(updates, id, beating.Token);
        try
        {
            return await UpdateAsync(id, updates, dataSource, source, dataFolder, connectionString, osm2pgsqlExe, terrain, rightsOfWay, leastRoomGb, output, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await End(updates, id, "failed", "Stopped. The map in use is as it was, or, if it was built, the next update finishes it.", output);
            return 1;
        }
        catch (Exception e) when (e is ExtractNotDownloadedException)
        {
            await End(updates, id, "failed", e.Message, output);
            return 1;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or NpgsqlException or TaskCanceledException)
        {
            await End(updates, id, "failed", $"The update failed, and the map in use is as it was: {e.Message}", output);
            return 1;
        }
        finally
        {
            await beating.CancelAsync();
            await heartbeat;
        }
    }

    private static async Task<int> UpdateAsync(
        int id, Jobs updates, NpgsqlDataSource dataSource, string source, string dataFolder, string connectionString,
        string? osm2pgsqlExe, string? terrain, string rightsOfWay, double leastRoomGb, TextWriter output, CancellationToken cancellationToken)
    {
        // A run that died after its swap: what follows the build finished, and nothing downloaded.
        if (await updates.LastUpdateBeforeAsync(id, cancellationToken) is { BuiltButNotFinished: true } stopped)
        {
            await output.WriteLineAsync($"The update of {stopped.StartedAt.ToLocalTime().ToString("d MMM HH:mm", Culture)} stopped after its map was built: finishing it.");
            await updates.StageAsync(id, "built", stopped.SourceAt, cancellationToken);
            await FinishAsync(id, updates, dataSource, connectionString, terrain, output, cancellationToken);
            await updates.EndAsync(stopped.Id, "failed", $"Stopped after its map was built; finished by the update after it.", cancellationToken);
            await End(updates, id, "finished", "Finished the update that stopped after its map was built.", output);
            return 0;
        }

        var extract = new ExtractSource(Http, source);
        await updates.StageAsync(id, "checking", cancellationToken: cancellationToken);
        var sourceAt = await extract.DataAtAsync(cancellationToken);
        await updates.StageAsync(id, "checking", sourceAt, cancellationToken);
        var newest = Latest(await updates.NewestSourceAtAsync(cancellationToken), await new PostgisNetworkDate(dataSource).OsmDataAtAsync(cancellationToken));
        if (newest is { } now && sourceAt <= now)
        {
            await End(updates, id, "up to date", $"The map is up to date: its paths are as of {Day(now)}.", output);
            return 0;
        }
        await output.WriteLineAsync($"  the source's map is of {Day(sourceAt)}{(newest is { } was ? $", the map in use of {Day(was)}" : "")}: downloading");

        // Room for it first (T129): a build that runs out of disk at hour two helps nobody.
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dataFolder))!);
        var freeGb = drive.AvailableFreeSpace / 1e9;
        if (freeGb < leastRoomGb)
        {
            await End(updates, id, "failed", string.Format(Culture,
                "Not enough room to update the map: {0:0} GB free on {1}, and it needs {2:0} GB. The map in use is as it was.", freeGb, drive.Name, leastRoomGb), output);
            return 1;
        }

        await updates.StageAsync(id, "downloading", cancellationToken: cancellationToken);
        var kept = await extract.DownloadAsync(dataFolder, cancellationToken: cancellationToken);
        await output.WriteLineAsync($"  downloaded {new FileInfo(kept).Length:N0} bytes, matching its MD5");

        await updates.StageAsync(id, "building", cancellationToken: cancellationToken);
        var built = false;
        var code = await NetworkBuildCommand.Run(kept, connectionString, osm2pgsqlExe, terrain, rightsOfWay, output, locked: true,
            built: async () =>
            {
                built = true;
                await updates.StageAsync(id, "built", cancellationToken: cancellationToken);
            }, cancellationToken: cancellationToken);
        if (code != 0)
        {
            await End(updates, id, "failed", built
                ? "The map was built, but its Outings weren't all scored: the next update finishes them."
                : "The map couldn't be built from the new extract, so the map in use is as it was. The log says why.", output);
            return 1;
        }

        await FinishAsync(id, updates, dataSource, connectionString, terrain, output, cancellationToken);
        await End(updates, id, "finished", $"Updated: the map's paths are as of {Day(sourceAt)}.", output);
        return 0;
    }

    private static Task FinishAsync(int id, Jobs updates, NpgsqlDataSource dataSource, string connectionString, string? terrain, TextWriter output, CancellationToken cancellationToken) =>
        FinishAsync(stage => updates.StageAsync(id, stage, cancellationToken: cancellationToken), dataSource, connectionString, terrain, output, cancellationToken);

    /// <summary>
    /// What follows a new map swapped in, built or gone back to (T131): Saved Routes the map changed
    /// under moved onto it (T136), any Outing or Saved Route still unscored scored, the stars
    /// calibrated, a backup; each stage told as it's reached.
    /// </summary>
    internal static async Task FinishAsync(Func<string, Task> stage, NpgsqlDataSource dataSource, string connectionString, string? terrain, TextWriter output, CancellationToken cancellationToken)
    {
        await stage("moving Saved Routes");
        try
        {
            await RoutesMoveCommand.MoveAsync(dataSource, terrain, output, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException and not NpgsqlException)
        {
            // Kept as they were, flagged: the update goes on.
            await output.WriteLineAsync($"  Saved Routes not moved: {e.Message}");
        }
        await OutingScoring.ScoreAsync(dataSource, cancellationToken: cancellationToken);
        await OutingScoring.ScoreSavedRoutesAsync(dataSource, cancellationToken);
        await stage("calibrating");
        var calibration = await StarCalibration.CalibrateAsync(dataSource, cancellationToken);
        await output.WriteLineAsync(string.Format(Culture, "  stars calibrated: Runs from {0:N0}, rides from {1:N0}", calibration.Run.From, calibration.Ride.From));
        await stage("backing up");
        await BackupCommand.Then(0, connectionString, output, cancellationToken);
    }

    private static async Task End(Jobs updates, int id, string outcome, string message, TextWriter output)
    {
        await updates.EndAsync(id, outcome, message, CancellationToken.None);
        await output.WriteLineAsync(message);
    }

    // Alive, said every minute while it runs, for the app to tell a running update from a stopped one.
    private static async Task BeatAsync(Jobs updates, int id, CancellationToken stop)
    {
        using var every = new PeriodicTimer(HeartbeatEvery);
        try
        {
            while (await every.WaitForNextTickAsync(stop)) await updates.HeartbeatAsync(id, stop);
        }
        catch (OperationCanceledException)
        {
            // Finished.
        }
        catch (NpgsqlException)
        {
            // Unsaid this time; the run goes on.
        }
    }

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : a > b ? a : b;

    private static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("d MMM yyyy", Culture);

    // One client for the run: no timeout for a 2 GB download, which the run's own token can stop.
    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", "WendYourWay-map-update/1.0" } },
    };

    /// <summary>A log file the run's words are written to as well, kept as they're written.</summary>
    public static StreamWriter OpenLog(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        return new StreamWriter(file, append: true, Encoding.UTF8) { AutoFlush = true };
    }
}

/// <summary>Words written to two places at once: the terminal and a log.</summary>
internal sealed class Both(TextWriter first, TextWriter second) : TextWriter
{
    public override Encoding Encoding => first.Encoding;

    public override void Write(char value)
    {
        first.Write(value);
        second.Write(value);
    }

    public override void Write(string? value)
    {
        first.Write(value);
        second.Write(value);
    }

    public override void WriteLine(string? value)
    {
        first.WriteLine(value);
        second.WriteLine(value);
    }

    public override Task WriteLineAsync(string? value)
    {
        WriteLine(value);
        return Task.CompletedTask;
    }

    public override void Flush()
    {
        first.Flush();
        second.Flush();
    }
}
