using System.IO.Compression;

namespace WendYourWay.Data;

/// <summary>
/// Which release of OS Terrain 50 a folder of its tiles is (T171, #175): said in a file beside them, written
/// as they're unpacked, so the update knows whether OS has a newer one and the Admin panel can say which is
/// in use. A folder unpacked by hand says none.
/// </summary>
/// <param name="Version">OS's version, "2026-07".</param>
/// <param name="Md5">The MD5 of the zip they came from.</param>
public sealed record TerrainRelease(string Version, string Md5)
{
    /// <summary>The file it's said in, which <see cref="TerrainGrid"/> passes over.</summary>
    public const string FileName = "release.txt";

    /// <summary>The release the folder's tiles are; null if it doesn't say.</summary>
    public static TerrainRelease? In(string folder)
    {
        var file = Path.Combine(folder, FileName);
        if (!File.Exists(file)) return null;
        var said = File.ReadAllLines(file).Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.Ordinal);
        return said.TryGetValue("version", out var version) && said.TryGetValue("md5", out var md5) ? new TerrainRelease(version, md5) : null;
    }

    /// <summary>
    /// Unpacks OS Terrain 50's Great Britain zip into <paramref name="folder"/>, in place of anything there:
    /// the ASCII grid tile of each square's own zip, and <paramref name="release"/> said beside them, last.
    /// Returns how many tiles.
    /// </summary>
    public static async Task<int> UnpackAsync(string zip, string folder, TerrainRelease release, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        var tiles = 0;
        await using var outer = await ZipFile.OpenReadAsync(zip, cancellationToken);
        foreach (var square in outer.Entries.Where(e => e.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            await using var squareStream = await square.OpenAsync(cancellationToken);
            await using var inner = new ZipArchive(squareStream, ZipArchiveMode.Read);
            foreach (var tile in inner.Entries.Where(e => e.Name.EndsWith(".asc", StringComparison.OrdinalIgnoreCase)))
            {
                await tile.ExtractToFileAsync(Path.Combine(folder, tile.Name), overwrite: true, cancellationToken);
                tiles++;
            }
        }
        await File.WriteAllLinesAsync(Path.Combine(folder, FileName), [$"version={release.Version}", $"md5={release.Md5}"], cancellationToken);
        return tiles;
    }
}
