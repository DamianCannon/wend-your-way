using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace WendYourWay.Data.Building;

/// <summary>One of OS's open products, and the format of its Great Britain file used (T171).</summary>
/// <param name="Id">The Downloads API's name for it.</param>
/// <param name="Format">The format of the file used.</param>
/// <param name="Called">What it's called where it's said.</param>
public sealed record OsProduct(string Id, string Format, string Called);

/// <summary>A product's release: its version, and its Great Britain file (T171).</summary>
public sealed record OsRelease(OsProduct Product, string Version, string FileName, string Md5, long Size, Uri Url);

/// <summary>
/// OS's Downloads API (T171, #175): its open products, free and without a key. Each has a version (OS Open
/// Names "2026-10", quarterly; OS Terrain 50 "2026-07", yearly), and its files, each with its MD5.
/// </summary>
public sealed class OsDownloads(HttpClient http, string api = OsDownloads.Api)
{
    /// <summary>OS's Downloads API.</summary>
    public const string Api = "https://api.os.uk/downloads/v1/";

    /// <summary>OS Open Names, for searching places: its CSV.</summary>
    public static readonly OsProduct Names = new("OpenNames", "CSV", "OS Open Names");

    /// <summary>OS Terrain 50, for heights: its ASCII grid tiles.</summary>
    public static readonly OsProduct Heights = new("Terrain50", "ASCII Grid and GML (Grid)", "OS Terrain 50");

    private readonly Uri _api = new(api);

    /// <summary>The product's newest release, and its Great Britain file in the product's format.</summary>
    public async Task<OsRelease> ReleaseAsync(OsProduct product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        var about = await http.GetFromJsonAsync<About>(new Uri(_api, $"products/{product.Id}"), cancellationToken)
            ?? throw new InvalidOperationException($"OS said nothing of {product.Called}.");
        var files = await http.GetFromJsonAsync<FileAbout[]>(new Uri(_api, $"products/{product.Id}/downloads"), cancellationToken) ?? [];
        var file = files.FirstOrDefault(f => f.Format == product.Format && f.Area == "GB")
            ?? throw new InvalidOperationException($"OS has no {product.Format} of {product.Called} for Great Britain.");
        return new OsRelease(product, about.Version, file.FileName, file.Md5, file.Size, new Uri(file.Url));
    }

    /// <summary>
    /// The release's file in <paramref name="folder"/>, by its name: kept as it is if it has the release's MD5,
    /// else downloaded to a `.part` file, checked against the MD5, and only then put in place. Anything wrong
    /// leaves what was there as it was. Returns where it's kept.
    /// </summary>
    /// <param name="stall">How long without a byte before it's given up: 2 minutes by default.</param>
    /// <exception cref="ExtractNotDownloadedException">It didn't arrive whole, stalled, or didn't match its MD5.</exception>
    public async Task<string> DownloadAsync(OsRelease release, string folder, TimeSpan? stall = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        Directory.CreateDirectory(folder);
        var kept = Path.Combine(folder, release.FileName);
        if (File.Exists(kept) && await Md5Async(kept, cancellationToken) == release.Md5) return kept;

        var quiet = stall ?? TimeSpan.FromMinutes(2);
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stalled.CancelAfter(quiet);
        var part = kept + ".part";
        try
        {
            using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, stalled.Token);
            response.EnsureSuccessStatusCode();
#pragma warning disable CA5351 // MD5 because OS publishes it: a check the file arrived whole, not of who sent it.
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.MD5);
#pragma warning restore CA5351
            long written = 0;
            await using (var from = await response.Content.ReadAsStreamAsync(stalled.Token))
            await using (var to = File.Create(part))
            {
                var buffer = new byte[1 << 20];
                int read;
                while ((read = await from.ReadAsync(buffer, stalled.Token)) > 0)
                {
                    stalled.CancelAfter(quiet);
                    hash.AppendData(buffer, 0, read);
                    await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                }
            }
            if (written != release.Size)
                throw new ExtractNotDownloadedException($"{release.Product.Called}'s download stopped part-way ({written:N0} of {release.Size:N0} bytes). Try again.");
            if (Convert.ToHexStringLower(hash.GetHashAndReset()) != release.Md5)
                throw new ExtractNotDownloadedException($"{release.Product.Called}'s download didn't match its checksum. Try again.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            File.Delete(part);
            throw new ExtractNotDownloadedException($"{release.Product.Called}'s download failed: {e.Message} Try again.", e);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            File.Delete(part);
            throw new ExtractNotDownloadedException($"{release.Product.Called}'s download stalled: nothing came for {quiet.TotalMinutes:0.#} minutes. Try again.", e);
        }
        catch (ExtractNotDownloadedException)
        {
            File.Delete(part);
            throw;
        }
        File.Move(part, kept, overwrite: true);
        return kept;
    }

    /// <summary>A file's MD5, as OS gives it.</summary>
    public static async Task<string> Md5Async(string file, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(file);
#pragma warning disable CA5351 // As OS publishes it.
        return Convert.ToHexStringLower(await System.Security.Cryptography.MD5.HashDataAsync(stream, cancellationToken));
#pragma warning restore CA5351
    }

    private sealed record About([property: JsonPropertyName("version")] string Version);

    private sealed record FileAbout(
        [property: JsonPropertyName("md5")] string Md5, [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("url")] string Url, [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("area")] string Area, [property: JsonPropertyName("fileName")] string FileName);
}
