using WendYourWay.Data;
using WendYourWay.Data.Building;

namespace WendYourWay.Cli;

/// <summary>
/// The `wend` command. Exit codes: 0 success, 1 the command failed, 2 the command line was wrong.
/// </summary>
public static class CommandLine
{
    public const string Usage = """
        Usage:
          wend network build <extract.osm.pbf> [--database <connection string>] [--osm2pgsql <path>] [--terrain <folder>] [--rights-of-way <folder>]
              Rebuild the Network from an OpenStreetMap extract (Geofabrik .osm.pbf or .osm XML).
          wend network build --region <region> [--data <folder>] [--database <connection string>] [--terrain <folder>] [--rights-of-way <folder>]
              Download a region of England or Wales from Geofabrik (a county, say: surrey) and build the Network from it.
          wend practice [--rebuild] [--database <connection string>] [--osm2pgsql <path>]
              Build the practice map (Richmond Park and around, shipped with the app) in its own database beside
              the app's (<name>_practice), to try Wend Your Way on before building a region; --rebuild builds it again.
          wend byways get [--folder <folder>]
              Download the highway authorities' Definitive Map byways, for Trail Rides, from rowmaps.com (about 10 MB;
              default data/rights-of-way). Run again to refresh.
          wend network regions
              List the regions of England and Wales Geofabrik publishes.
          wend network rebuild [--database <connection string>] [--terrain <folder>] [--rights-of-way <folder>]
              Rebuild the Network from the extract already loaded, after a rule changes.
              --terrain: OS Terrain 50's ASCII grid tiles, for climbing (default data/terrain50/asc).
              --rights-of-way: the Definitive Map byways, for Trail Rides, one folder per authority
                (default data/rights-of-way).
          wend update [--source <extract url>] [--data <folder>] [--database <connection string>] [--terrain <folder>] [--os <url>] [--log <file>]
              Bring the map up to date: if Geofabrik's Great Britain extract is newer than the map, download it
              (checked against its MD5, the one before kept), build the Network from it, and calibrate the stars.
              OS's heights (Terrain 50) and place names (Open Names) come with it when OS has newer ones, from its
              Downloads API (--os: another like it); if they can't, those in use are kept.
              Whatever fails part-way, the map in use stays as it was; a run stopped after its map was built is
              finished by the next. One at a time; --log writes what it says to a file too.
          wend network back [--database <connection string>] [--terrain <folder>] [--log <file>]
              Go back to the map before, if a new one turns out bad (a build keeps the one before it); done again,
              it's forward again. The Outings are covered and scored again on it, Saved Routes moved onto it,
              the stars calibrated.
          wend network offroad [--database <connection string>]
              Find the Off-road networks, Trailheads, Obstacles and Gravel, Trail Ride and Run hotspots again on the Network as it is, without a rebuild.
          wend outings import-gpx <file-or-folder> [--database <connection string>]
              Import your own runs and rides from GPX, TCX or FIT files, and see what each covered.
          wend outings import-strava <export.zip> [--database <connection string>]
              Import your whole history from Strava's bulk export (read in place).
          wend outings score [--all] [--database <connection string>]
              Score the Outings not scored yet, for the app's stars; --all scores every one again.
          wend outings calibrate [--database <connection string>]
              Work the stars' cut points out again from your Outings as they are, and keep them.
          wend routes move [--database <connection string>]
              Move each Saved Route the map has changed under onto the map as it is, if it follows the same line
              (wend update does this after it builds); the rest are kept as they were, saying where they part.
          wend outings import-downloads [--folder <folder>] [--database <connection string>] [--log <file>]
              Import new runs from Downloads (or the folder given): the newest Strava export there, and every GPX,
              TCX or FIT that's a recording. Planned routes, ones already in and ones you deleted are skipped.
          wend places import <folder> [--database <connection string>]
              Load OS Open Names' places and postcodes (its CSV download, read in place), to search for by name and postcode.
          wend backup [folder] [--database <connection string>]
              Back up your own data (Outings, stars, Saved Routes, Marks, settings) to a dated file; 10 are kept, spread over a month.
              Default folder: WEND_BACKUP_FOLDER, else "Wend Your Way Backups" in OneDrive. Imports, scoring and
              builds back up by themselves when they finish.
          wend restore <backup.dump> [--database <connection string>] [--log <file>]
              Put a backup's data back, in place of what's there (which is backed up first). Waits its turn
              if another job is running.
        """;

    /// <param name="cancellationToken">Ctrl+C: stops the command, and its work in PostgreSQL with it.</param>
    public static async Task<int> Run(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        // The byways for Trail Rides (T142), on any machine.
        if (args is ["byways", "get", .. var byways] && Options.TryParse(byways, out var bywayOptions))
            return await BywaysCommand.Get(bywayOptions.GetValueOrDefault("--folder", DefaultRightsOfWay), bywayOptions.GetValueOrDefault("--source", BywaysCommand.Source),
                TimeSpan.FromMilliseconds(double.Parse(bywayOptions.GetValueOrDefault("--pause", "300"), System.Globalization.CultureInfo.InvariantCulture)), output, cancellationToken);

        // The practice map (T161): Richmond Park and around, in its own database.
        if (args is ["practice", .. var practising])
        {
            var rebuildPractice = practising.Contains("--rebuild");
            if (Options.TryParse([.. practising.Where(a => a != "--rebuild")], out var practiceOptions))
                return await PracticeCommand.Run(practiceOptions.GetValueOrDefault("--database", UsualDatabase),
                    practiceOptions.GetValueOrDefault("--osm2pgsql") ?? Osm2pgsqlLocator.Find(), rebuildPractice, output, cancellationToken);
        }

        // A region of your own (T143): its extract downloaded from Geofabrik, then built as below.
        if (args is ["network", "regions", .. var listing] && Options.TryParse(listing, out var listOptions))
            return await RegionCommand.List(listOptions.GetValueOrDefault("--index", GeofabrikRegions.Index), output, cancellationToken);

        if (args is ["network", "build", "--region", var region, .. var regionRest] && Options.TryParse(regionRest, out var regionOptions))
        {
            var database = regionOptions.GetValueOrDefault("--database", UsualDatabase);
            if (await RegionCommand.DownloadAsync(region, regionOptions.GetValueOrDefault("--index", GeofabrikRegions.Index), regionOptions.GetValueOrDefault("--data", "data"), output, cancellationToken) is not { } downloaded)
                return 1;
            return await BackupCommand.Then(await NetworkBuildCommand.Run(
                downloaded, database,
                regionOptions.GetValueOrDefault("--osm2pgsql") ?? Osm2pgsqlLocator.Find(),
                Terrain(regionOptions), regionOptions.GetValueOrDefault("--rights-of-way", DefaultRightsOfWay), output, cancellationToken: cancellationToken), database, output, cancellationToken);
        }

        if (args is ["network", "build", var extract, .. var rest] && Options.TryParse(rest, out var options))
        {
            var database = options.GetValueOrDefault("--database", UsualDatabase);
            return await BackupCommand.Then(await NetworkBuildCommand.Run(
                extract, database,
                options.GetValueOrDefault("--osm2pgsql") ?? Osm2pgsqlLocator.Find(),
                Terrain(options), options.GetValueOrDefault("--rights-of-way", DefaultRightsOfWay), output, cancellationToken: cancellationToken), database, output, cancellationToken);
        }

        // The map brought up to date in one go (T127): a newer extract checked, downloaded, built, and
        // its stars calibrated; backed up as it finishes. Written to --log too, when started by the app.
        if (args is ["update", .. var updating] && Options.TryParse(updating, out var updateOptions))
        {
            var database = updateOptions.GetValueOrDefault("--database", UsualDatabase);
            using var log = updateOptions.GetValueOrDefault("--log") is { } logFile ? UpdateCommand.OpenLog(logFile) : null;
            var to = log is null ? output : new Both(output, log);
            return await UpdateCommand.Run(
                updateOptions.GetValueOrDefault("--source", Data.Building.ExtractSource.GreatBritain), updateOptions.GetValueOrDefault("--data", "data"), database,
                updateOptions.GetValueOrDefault("--osm2pgsql") ?? Osm2pgsqlLocator.Find(), Terrain(updateOptions),
                updateOptions.GetValueOrDefault("--rights-of-way", DefaultRightsOfWay), to,
                updateOptions.TryGetValue("--room", out var room) ? double.Parse(room, System.Globalization.CultureInfo.InvariantCulture) : UpdateCommand.LeastRoomGb,
                updateOptions.GetValueOrDefault("--os", Data.Building.OsDownloads.Api), cancellationToken);
        }

        if (args is ["network", "rebuild", .. var again] && Options.TryParse(again, out var rebuildOptions))
        {
            var database = rebuildOptions.GetValueOrDefault("--database", UsualDatabase);
            return await BackupCommand.Then(await NetworkBuildCommand.Run(
                extract: null, database, osm2pgsqlExe: null,
                Terrain(rebuildOptions), rebuildOptions.GetValueOrDefault("--rights-of-way", DefaultRightsOfWay), output, cancellationToken: cancellationToken), database, output, cancellationToken);
        }

        // Back to the map before (T131), run as a job; done again, forward again.
        if (args is ["network", "back", .. var back] && Options.TryParse(back, out var backOptions))
        {
            using var log = backOptions.GetValueOrDefault("--log") is { } logFile ? UpdateCommand.OpenLog(logFile) : null;
            return await NetworkBackCommand.Run(backOptions.GetValueOrDefault("--database", UsualDatabase), Terrain(backOptions),
                log is null ? output : new Both(output, log), cancellationToken);
        }

        if (args is ["network", "offroad", .. var offRoad] && Options.TryParse(offRoad, out var offRoadOptions))
        {
            return await NetworkBuildCommand.RunOffRoad(offRoadOptions.GetValueOrDefault("--database", UsualDatabase), output, cancellationToken);
        }

        if (args is ["outings", "import-gpx", var gpx, .. var more] && Options.TryParse(more, out var importOptions))
        {
            var database = importOptions.GetValueOrDefault("--database", UsualDatabase);
            return await BackupCommand.Then(await OutingsImportCommand.Run(gpx, database, output, cancellationToken), database, output, cancellationToken);
        }

        // Runs from Downloads (T130): the newest Strava export and recorded loose files, run as a job.
        if (args is ["outings", "import-downloads", .. var fromDownloads] && Options.TryParse(fromDownloads, out var downloadsOptions))
        {
            using var log = downloadsOptions.GetValueOrDefault("--log") is { } logFile ? UpdateCommand.OpenLog(logFile) : null;
            return await DownloadsImportCommand.Run(downloadsOptions.GetValueOrDefault("--folder", DownloadsImportCommand.Downloads),
                downloadsOptions.GetValueOrDefault("--database", UsualDatabase), log is null ? output : new Both(output, log), cancellationToken);
        }

        if (args is ["outings", "import-strava", var zip, .. var others] && Options.TryParse(others, out var stravaOptions))
        {
            var database = stravaOptions.GetValueOrDefault("--database", UsualDatabase);
            return await BackupCommand.Then(await StravaImportCommand.Run(zip, database, output, cancellationToken), database, output, cancellationToken);
        }

        if (args is ["outings", "score", .. var scoring])
        {
            var all = scoring.Contains("--all");
            if (Options.TryParse([.. scoring.Where(a => a != "--all")], out var scoreOptions))
            {
                var database = scoreOptions.GetValueOrDefault("--database", UsualDatabase);
                return await BackupCommand.Then(await OutingsScoreCommand.Run(database, all, output, cancellationToken), database, output, cancellationToken);
            }
        }

        if (args is ["outings", "calibrate", .. var calibrating] && Options.TryParse(calibrating, out var calibrateOptions))
        {
            var database = calibrateOptions.GetValueOrDefault("--database", UsualDatabase);
            return await BackupCommand.Then(await OutingsCalibrateCommand.Run(database, output, cancellationToken), database, output, cancellationToken);
        }

        // Saved Routes moved onto the map as it is (T136): no lock, no backup of its own.
        if (args is ["routes", "move", .. var moving] && Options.TryParse(moving, out var moveOptions))
            return await RoutesMoveCommand.Run(moveOptions.GetValueOrDefault("--database", UsualDatabase), Terrain(moveOptions), output, cancellationToken);

        if (args is ["places", "import", var names, .. var placesRest] && Options.TryParse(placesRest, out var placesOptions))
        {
            return await PlacesImportCommand.Run(names, placesOptions.GetValueOrDefault("--database", UsualDatabase), output, cancellationToken);
        }

        // `wend backup [folder]`: the folder, if given, comes before any options.
        if (args is ["backup", .. var backing])
        {
            var folder = backing is [var first, ..] && !first.StartsWith("--", StringComparison.Ordinal) ? first : null;
            if (Options.TryParse(folder is null ? backing : backing[1..], out var backupOptions))
                return await BackupCommand.Backup(folder, backupOptions.GetValueOrDefault("--database", UsualDatabase), output, cancellationToken);
        }

        if (args is ["restore", var dump, .. var restoring] && Options.TryParse(restoring, out var restoreOptions))
        {
            using var log = restoreOptions.GetValueOrDefault("--log") is { } logFile ? UpdateCommand.OpenLog(logFile) : null;
            return await BackupCommand.Restore(dump, restoreOptions.GetValueOrDefault("--database", UsualDatabase), log is null ? output : new Both(output, log), cancellationToken);
        }

        await output.WriteLineAsync(Usage);
        return 2;
    }

    // The terrain folder asked for, or the usual one if it's there; none if not, and the build says so.
    private static string? Terrain(Dictionary<string, string> options) =>
        options.GetValueOrDefault("--terrain") ?? (Directory.Exists(DefaultTerrain) ? DefaultTerrain : null);

    private const string DefaultTerrain = "data/terrain50/asc";

    private const string DefaultRightsOfWay = "data/rights-of-way";

    /// <summary>The database a command uses without --database: WEND_DATABASE if set (Docker's, T142), else the app's own.</summary>
    private static string UsualDatabase => Environment.GetEnvironmentVariable("WEND_DATABASE") is { Length: > 0 } set ? set : Database.DefaultConnectionString;

    private static class Options
    {
        private static readonly HashSet<string> Known = ["--database", "--osm2pgsql", "--terrain", "--rights-of-way", "--source", "--data", "--log", "--room", "--os", "--folder", "--index", "--pause"];

        public static bool TryParse(string[] args, out Dictionary<string, string> options)
        {
            options = [];
            for (var i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !Known.Contains(args[i])) return false;
                options[args[i]] = args[i + 1];
            }
            return true;
        }
    }
}
