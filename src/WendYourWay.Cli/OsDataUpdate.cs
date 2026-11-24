using System.Globalization;
using System.Text.Json;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Places;
using Npgsql;

namespace WendYourWay.Cli;

/// <summary>
/// OS's open data brought up to date with the map (T171, #175; the user chose to "Create all four" of the OS
/// Data Hub ideas): OS Terrain 50's heights, yearly, and OS Open Names' place names, quarterly, from OS's
/// Downloads API, each only when OS has a release newer than the one in use. Neither stops the map's update:
/// out of reach or gone wrong, what's in use is kept, and it's said.
/// </summary>
public static class OsDataUpdate
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>The folder OS Open Names' zip is kept in, in the data folder.</summary>
    public const string NamesFolder = "opennames";

    /// <summary>
    /// Heights for the build (T171): the tiles in use if they're OS's newest release; else OS's newest unpacked
    /// beside them, `{terrain}-new`, for the build to climb on and <see cref="UseHeights"/> to swap in once it's
    /// built. Returns the folder to build with.
    /// </summary>
    public static async Task<string> HeightsAsync(OsDownloads os, string terrain, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(output);
        try
        {
            var release = await os.ReleaseAsync(OsDownloads.Heights, cancellationToken);
            var inUse = TerrainRelease.In(terrain);
            var said = new TerrainRelease(release.Version, release.Md5);
            if (inUse?.Md5 == release.Md5)
            {
                await output.WriteLineAsync($"  heights: {release.Product.Called} {release.Version}, OS's newest");
                return terrain;
            }
            // Its zip kept beside the tiles: fetched only if it isn't OS's newest (as unzipped by hand before T171, it is).
            var kept = await os.DownloadAsync(release, Path.GetDirectoryName(Path.GetFullPath(terrain))!, cancellationToken: cancellationToken);
            var unpacked = New(terrain);
            var tiles = await TerrainRelease.UnpackAsync(kept, unpacked, said, cancellationToken);
            await output.WriteLineAsync(string.Format(Culture, "  heights: {0} {1}, {2:N0} {3}", release.Product.Called, release.Version, tiles, tiles == 1 ? "tile" : "tiles"));
            return unpacked;
        }
        catch (Exception e) when (Missed(e, cancellationToken))
        {
            await output.WriteLineAsync($"  heights not updated: {e.Message} The heights in use are kept.");
            return terrain;
        }
    }

    /// <summary>
    /// The heights the build climbed on put in use (T171), if they're new: in place of those before. None
    /// new, or the map not built, and they're left as they are, a new folder cleared away.
    /// </summary>
    public static async Task UseHeights(string terrain, string builtWith, bool built, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (builtWith == terrain) return;
        try
        {
            if (!built)
            {
                Directory.Delete(builtWith, recursive: true);
                return;
            }
            var before = terrain + "-prev";
            if (Directory.Exists(before)) Directory.Delete(before, recursive: true);
            if (Directory.Exists(terrain)) Directory.Move(terrain, before);
            Directory.Move(builtWith, terrain);
            Directory.Delete(before, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync($"  the new heights couldn't be put in use: {e.Message} They're at {builtWith}.");
        }
    }

    /// <summary>
    /// OS Open Names' place names (T171), loaded after the build as they take the map's green areas: OS's newest
    /// release, fetched into the data folder's <see cref="NamesFolder"/> and loaded, unless it's the one in use.
    /// </summary>
    public static async Task PlaceNamesAsync(OsDownloads os, NpgsqlDataSource dataSource, string dataFolder, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(output);
        try
        {
            var release = await os.ReleaseAsync(OsDownloads.Names, cancellationToken);
            if ((await PlacesImport.ReleaseAsync(dataSource, cancellationToken))?.Release.Md5 == release.Md5)
            {
                await output.WriteLineAsync($"  place names: {release.Product.Called} {release.Version}, OS's newest");
                return;
            }
            var kept = await os.DownloadAsync(release, Path.Combine(dataFolder, NamesFolder), cancellationToken: cancellationToken);
            var loaded = await PlacesImport.LoadZipAsync(dataSource, kept, new PlacesRelease(release.Version, release.Md5), cancellationToken: cancellationToken);
            await output.WriteLineAsync(string.Format(Culture, "  place names: {0} {1} loaded, {2:N0} places", release.Product.Called, release.Version, loaded.Places));
        }
        catch (Exception e) when (Missed(e, cancellationToken))
        {
            await output.WriteLineAsync($"  place names not updated: {e.Message} The place names in use are kept.");
        }
    }

    // Where OS's newest heights are unpacked, beside those in use.
    private static string New(string terrain) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(terrain)) + "-new";

    // What OS out of reach, or its files gone wrong, looks like: not the update stopped.
    private static bool Missed(Exception e, CancellationToken cancellationToken) =>
        e is HttpRequestException or ExtractNotDownloadedException or IOException or InvalidDataException or JsonException
            or InvalidOperationException or UnauthorizedAccessException
        || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
