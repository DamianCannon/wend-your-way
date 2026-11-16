using System.Diagnostics;
using System.Globalization;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Cli;

/// <summary>
/// `wend network build`: migrate the database, load the extract with osm2pgsql, cut it into
/// Segments and find the paths beside roads (together, all or nothing), and report what
/// happened, phase by phase, with everything that was skipped.
/// </summary>
public static class NetworkBuildCommand
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <param name="extract">The extract to load first, or null to rebuild from what's already loaded (`wend network rebuild`).</param>
    /// <param name="terrain">The folder of OS Terrain 50 tiles for climbing, or null to build without it.</param>
    /// <param name="rightsOfWay">The folder of Definitive Map byways, one folder per authority (T021): required, so no Network silently lacks them.</param>
    /// <param name="locked">Whether the caller holds the update lock (`wend update`, T127); if not, it's taken here.</param>
    /// <param name="built">Told once the new Network is swapped in, before the Outings are scored (T127).</param>
    public static async Task<int> Run(
        string? extract, string connectionString, string? osm2pgsqlExe, string? terrain, string rightsOfWay, TextWriter output,
        bool locked = false, Func<Task>? built = null, Action<string>? phaseDone = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (terrain is not null && !Directory.Exists(terrain))
        {
            await output.WriteLineAsync($"No OS Terrain 50 tiles at {Path.GetFullPath(terrain)}. Unzip its ASCII grid there, or leave out --terrain.");
            return 1;
        }
        if (extract is not null && !File.Exists(extract))
        {
            await output.WriteLineAsync($"No extract at {Path.GetFullPath(extract)}. Download one from https://download.geofabrik.de into data/.");
            return 1;
        }
        if (!Directory.Exists(rightsOfWay) || !Directory.EnumerateFiles(rightsOfWay, RightsOfWayPhase.BywayFile, SearchOption.AllDirectories).Any())
        {
            await output.WriteLineAsync($"No Definitive Map byways at {Path.GetFullPath(rightsOfWay)}, for Trail Rides. Download each authority's from {RightsOfWayPhase.Source} into a folder of its code there, or pass --rights-of-way <folder>.");
            return 1;
        }
        if (extract is not null && (osm2pgsqlExe is null || !File.Exists(osm2pgsqlExe)))
        {
            await output.WriteLineAsync("osm2pgsql not found. Run scripts/setup.ps1, or pass --osm2pgsql <path>.");
            return 1;
        }

        // One build or map update at a time (T127).
        // Taken whatever the token says (it takes no time): stopped, the build says so in its own words.
        await using var held = locked ? null : await UpdateLock.TryTakeAsync(connectionString, CancellationToken.None);
        if (!locked && held is null)
        {
            await output.WriteLineAsync(UpdateLock.Held);
            return 1;
        }

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await output.WriteLineAsync(extract is null
            ? $"Network rebuild: from the extract already loaded → {database}"
            : $"Network build: {Path.GetFileName(extract)} → {database}");

        try
        {
            var applied = await Timed(output, "migrations", () => Task.FromResult(Migrations.Apply(connectionString)),
                result => $"{result.Count} applied");
            await using var dataSource = Database.CreateDataSource(connectionString);
            if (extract is not null)
            {
                // Marked while it loads (T127): stopped part-way, a rebuild from it is refused.
                await OsmImportMarker.ImportingAsync(dataSource, cancellationToken);
                await Timed(output, "osm2pgsql", () => Task.FromResult(OsmImport.Run(osm2pgsqlExe!, connectionString, extract, cancellationToken: cancellationToken)),
                    _ => "loaded");
                phaseDone?.Invoke("osm2pgsql");
                await OsmImportMarker.ImportedAsync(dataSource, cancellationToken);
            }
            else if (await OsmImportMarker.UnfinishedAsync(dataSource, cancellationToken))
            {
                await output.WriteLineAsync(OsmImportMarker.Unfinished);
                return 1;
            }

            var grid = terrain is null ? null : new TerrainGrid(terrain);
            if (grid is null) await output.WriteLineAsync("  no OS Terrain 50 tiles (data/terrain50/asc): Segments won't climb");

            var report = await NetworkBuild.Build(dataSource, grid, (phase, took) => Done(phaseDone, phase, () => Phase(output, phase, phase switch
            {
                "rights of way" => "loaded",
                "segments" => "built",
                "same ground" => "found",
                "open ground" => "crossed",
                "off-road networks" => "joined",
                "trailheads" => "found",
                "obstacles" => "found",
                "gravel networks" => "joined",
                "in order of place" => "sorted",
                "climbing" => $"sampled ({grid!.Tiles:N0} tiles)",
                _ => "covered again",
            }, took)), rightsOfWay, cancellationToken);
            var (build, sameGround) = (report.Segments, report.SameGround);

            await output.WriteLineAsync();
            await Row(output, "ways read", build.WaysRead);
            await Row(output, "ways skipped (geometry and nodes disagree)", build.WaysMisaligned, build.WaysRead);
            if (report.RightsOfWay is { } rights)
            {
                await output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}  ({2:N0} km, {3:N0} authorities)",
                    "Definitive Map byways", rights.Byways, rights.LengthM / 1000, rights.Authorities));
                await Row(output, "authorities without an open Definitive Map", rights.WithoutData);
            }
            await Row(output, "Segments written", build.SegmentsWritten);
            await Row(output, "Segments skipped (identity clash)", build.IdentityClashes);
            await Row(output, "Segments skipped (zero length)", build.ZeroLength);
            if (build.Clipped)
            {
                await Row(output, "Segments skipped (in Scotland)", build.InScotland!.Value);
                await Row(output, "Segments skipped (in no country of Great Britain)", build.Elsewhere!.Value);
            }
            else
            {
                await output.WriteLineAsync(
                    $"  not clipped to England and Wales: the extract holds {(build.CountriesFound.Length == 0 ? "no country" : "only " + build.CountriesFound)} whole");
            }
            await Row(output, "paths beside roads (same ground)", sameGround.PathsBesideRoads, build.SegmentsWritten);
            await output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}  ({2:N0} km)",
                "Open ground crossings", report.OpenGround.Crossings, report.OpenGround.LengthM / 1000));
            await OffRoad(output, report.OffRoad, report.Trailheads, report.Obstacles, report.GravelNetworks);
            if (report.Climbing is { } climbing)
            {
                await output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}  ({2:N0} km of ascent)",
                    "Segments climbing", climbing.Segments, climbing.AscentM / 1000));
            }
            await Row(output, "Outings covered again", report.OutingsCovered);
            if (built is not null) await built();
            return await ScoreOutings(output, dataSource, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("Stopped. The Network is as it was.");
            return 1;
        }
        catch (Exception e) when (e is InvalidOperationException or NpgsqlException)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync($"Build failed: {e.Message}");
            return 1;
        }
    }

    // After the rebuild commits, the Outings it left unscored are scored on the new Network (T097),
    // in a phase of its own: stopped here, the Network is rebuilt and the scores are finished later.
    private static async Task<int> ScoreOutings(TextWriter output, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        try
        {
            var scores = await Timed(output, "outings scored", () => OutingScoring.ScoreAsync(dataSource, progress: OutingsScoreCommand.Progress(output), cancellationToken: cancellationToken),
                result => $"{result.Scored:N0} scored");
            await OutingsScoreCommand.Report(output, scores);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync($"Stopped while scoring Outings. The Network is rebuilt; what was scored is kept. {OutingsScoreCommand.Finish}");
            return 1;
        }
    }

    /// <summary>
    /// `wend network offroad`: finds the Off-road networks of the Network as it is, on their own,
    /// so a rule is measured without a rebuild (T022).
    /// </summary>
    public static async Task<int> RunOffRoad(string connectionString, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        await output.WriteLineAsync($"Off-road networks: {new NpgsqlConnectionStringBuilder(connectionString).Database}");
        try
        {
            await Timed(output, "migrations", () => Task.FromResult(Migrations.Apply(connectionString)), result => $"{result.Count} applied");
            await using var dataSource = Database.CreateDataSource(connectionString);
            var (built, trailheads, obstacles, gravel) = await Timed(output, "off-road networks", () => OffRoadNetworksPhase.Rebuild(dataSource, cancellationToken), _ => "joined, Trailheads, Obstacles and Gravel hotspots found");
            await output.WriteLineAsync();
            await OffRoad(output, built, trailheads, obstacles, gravel);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("Stopped. The Network is as it was.");
            return 1;
        }
        catch (Exception e) when (e is InvalidOperationException or NpgsqlException)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync($"Failed: {e.Message}");
            return 1;
        }
    }

    private static async Task OffRoad(TextWriter output, OffRoadBuild built, TrailheadBuild trailheads, ObstacleBuild? obstacles, GravelNetworkBuild? gravel)
    {
        await output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}  (largest {2:0.0} km)", "Off-road networks", built.Networks, built.LargestM / 1000));
        await output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}  (of {2:N0} car parks beside them)", "Trailheads", trailheads.Trailheads, trailheads.CarParks));
        if (obstacles is not null)
        {
            await output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}  ({2})", "Segments with Obstacles", obstacles.Segments,
                string.Join(", ", obstacles.SegmentsByKind.OrderByDescending(k => k.Value).Select(k => string.Format(Culture, "{0} {1:N0}", k.Key.Replace('_', ' '), k.Value)))));
        }
        if (gravel is not null)
        {
            await output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}  (of 25 km or more; largest {2:0.0} km)", "Gravel Off-road networks", gravel.Kept, gravel.LargestM / 1000));
        }
    }

    private static async Task<T> Timed<T>(TextWriter output, string phase, Func<Task<T>> work, Func<T, string> describe)
    {
        var clock = Stopwatch.StartNew();
        var result = await work();
        Phase(output, phase, describe(result), clock.Elapsed);
        return result;
    }

    // Told in the log, then to whoever asked to hear of each phase done (an update, counting its steps: T148).
    private static void Done(Action<string>? phaseDone, string phase, Action log)
    {
        log();
        phaseDone?.Invoke(phase);
    }

    private static void Phase(TextWriter output, string phase, string description, TimeSpan took) =>
        output.WriteLine(string.Format(Culture, "  {0,-18}{1,-22}{2,8:0.0} s", phase, description, took.TotalSeconds));

    private static Task Row(TextWriter output, string label, long value, long? outOf = null)
    {
        var share = outOf is > 0 ? string.Format(Culture, "  ({0:0.00}%)", 100.0 * value / outOf.Value) : "";
        return output.WriteLineAsync(string.Format(Culture, "  {0,-44}{1,12:N0}{2}", label, value, share));
    }
}
