using System.Globalization;
using System.Security.Cryptography;

namespace WendYourWay.Data.Building;

/// <summary>A download of an extract that can't be used: it didn't arrive whole, or didn't match its checksum.</summary>
public sealed class ExtractNotDownloadedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Where extracts come from (T127): Geofabrik's, or one like it. An extract at `…/{name}-latest.osm.pbf`
/// has its MD5 at the same address with `.md5` after it, and the date its data runs to in
/// `…/{name}-updates/state.txt`. Since November 2026 (#155, T151) Geofabrik keeps each in a folder of
/// its own, `…/{name}/{name}-latest.osm.pbf`, sending that on to the day's file with its MD5 beside
/// it (and, since, the `-latest` one's too, used when the day's isn't found), and the date in
/// `…/{name}/updates/state.txt`.
/// </summary>
public sealed class ExtractSource
{
    /// <summary>Great Britain, from Geofabrik: the one Wend Your Way builds from.</summary>
    public const string GreatBritain = "https://download.geofabrik.de/europe/great-britain/great-britain-latest.osm.pbf";

    private readonly HttpClient _http;
    private readonly Uri _extract;

    public ExtractSource(HttpClient http, string extractUrl)
    {
        ArgumentNullException.ThrowIfNull(extractUrl);
        _http = http;
        _extract = new Uri(extractUrl);
        var at = extractUrl.LastIndexOf("-latest", StringComparison.Ordinal);
        if (at < 0) throw new ArgumentException("An extract's address ends in -latest and its kind, as Geofabrik's do.", nameof(extractUrl));
        FileName = Path.GetFileName(_extract.LocalPath);
        // In a folder named as it is, the folder's updates; else beside it, as before.
        var named = FileName.LastIndexOf("-latest", StringComparison.Ordinal);
        var name = named < 0 ? null : FileName[..named];
        var folder = _extract.Segments.Length >= 2 ? _extract.Segments[^2].TrimEnd('/') : "";
        State = folder == name
            ? new Uri(_extract, "updates/state.txt")
            : new Uri(extractUrl[..at] + "-updates/state.txt");
        PreviousFileName = FileName.Replace("-latest", "-prev", StringComparison.Ordinal);
    }

    /// <summary>Where the date its data runs to is said.</summary>
    public Uri State { get; }

    /// <summary>The extract's file name, as it's kept.</summary>
    public string FileName { get; }

    /// <summary>The name the one before is kept by, to build from again if a new map turns out bad.</summary>
    public string PreviousFileName { get; }

    /// <summary>How many days back a missing day's file is looked for (T184).</summary>
    public const int DaysBack = 2;

    /// <summary>
    /// The day whose file was taken instead, when the one the extract was sent on to wasn't there (T184,
    /// #188: Geofabrik's wasn't, on 23 November 2026); none when the newest came.
    /// </summary>
    public DateOnly? TookEarlier { get; private set; }

    /// <summary>What's said when an earlier day's file was taken (T184).</summary>
    public static string Earlier(DateOnly day) =>
        $"  Geofabrik's newest file isn't there yet: using {day.ToString("d MMM", CultureInfo.InvariantCulture)}'s instead";

    // The extract, or where it's sent on to a day's file (`…-261122.osm.pbf`) that isn't found or ready, the day
    // before's, or the one before that: Geofabrik's day's file is sometimes listed before it's there.
    private async Task<HttpResponseMessage> DaysOrEarlierAsync(CancellationToken cancellationToken)
    {
        TookEarlier = null;
        var response = await _http.GetAsync(_extract, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        // Not found, or not ready (503: Geofabrik part-way through making it, 23 November 2026).
        if (response.StatusCode is not (System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.ServiceUnavailable)
            || response.RequestMessage?.RequestUri is not { } sent) return response;
        var dated = System.Text.RegularExpressions.Regex.Match(sent.AbsoluteUri, @"-(\d{6})(\.[^/]+)$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!dated.Success || !DateOnly.TryParseExact(dated.Groups[1].Value, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return response;
        for (var back = 1; back <= DaysBack; back++)
        {
            var earlier = day.AddDays(-back);
            var address = new Uri(sent.AbsoluteUri[..dated.Index] + "-" + earlier.ToString("yyMMdd", CultureInfo.InvariantCulture) + dated.Groups[2].Value);
            var tried = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (tried.IsSuccessStatusCode)
            {
                response.Dispose();
                TookEarlier = earlier;
                return tried;
            }
            tried.Dispose();
        }
        return response;
    }

    /// <summary>When the source's data runs to: `state.txt`'s timestamp, its colons escaped (`21\:22\:47`).</summary>
    public async Task<DateTimeOffset> DataAtAsync(CancellationToken cancellationToken = default)
    {
        var state = await _http.GetStringAsync(State, cancellationToken);
        var line = state.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("timestamp=", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"{State} says no timestamp.");
        return DateTimeOffset.Parse(line["timestamp=".Length..].Replace("\\:", ":", StringComparison.Ordinal), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    /// <summary>
    /// Downloads the extract into <paramref name="folder"/>: to a `.part` file first, checked against
    /// its MD5, and only then put in place, the one before kept as <see cref="PreviousFileName"/>.
    /// Anything wrong leaves what was there as it was. Returns where it's kept.
    /// </summary>
    /// <param name="stall">How long without a byte before it's given up (the laptop asleep, the line dead): 2 minutes by default.</param>
    /// <exception cref="ExtractNotDownloadedException">It didn't arrive whole, stalled, or didn't match its MD5.</exception>
    public async Task<string> DownloadAsync(string folder, TimeSpan? stall = null, CancellationToken cancellationToken = default)
    {
        var quiet = stall ?? TimeSpan.FromMinutes(2);
        // Given up if nothing comes for that long, put back each time something does (T127).
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stalled.CancelAfter(quiet);
        Directory.CreateDirectory(folder);
        var kept = Path.Combine(folder, FileName);
        var part = kept + ".part";
        try
        {
            using var response = await DaysOrEarlierAsync(stalled.Token);
            response.EnsureSuccessStatusCode();
            stalled.CancelAfter(quiet);
            // The MD5 beside the file that came: the day's, where it was sent on to one (T151). Where
            // that isn't found (#155, 22 November 2026), the extract's own `-latest` one: a day's turn
            // between the two shows as a mismatch, and nothing is put in place.
            var came = response.RequestMessage?.RequestUri ?? _extract;
            string sums;
            try
            {
                sums = await _http.GetStringAsync(new Uri(came + ".md5"), stalled.Token);
            }
            catch (HttpRequestException) when (came != _extract)
            {
                sums = await _http.GetStringAsync(new Uri(_extract + ".md5"), stalled.Token);
            }
            var md5 = sums.Split(' ', '\t', '\n')[0].Trim().ToLowerInvariant();
            stalled.CancelAfter(quiet);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
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
            if (response.Content.Headers.ContentLength is { } length && written != length)
                throw new ExtractNotDownloadedException($"The download stopped part-way ({written:N0} of {length:N0} bytes). Try again.");
            if (Convert.ToHexStringLower(hash.GetHashAndReset()) != md5)
                throw new ExtractNotDownloadedException(
                    "The download didn't match its checksum. Geofabrik may be part-way through making today's map, so try again later.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            File.Delete(part);
            throw new ExtractNotDownloadedException($"The download failed: {e.Message} Try again.", e);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            File.Delete(part);
            throw new ExtractNotDownloadedException($"The download stalled: nothing came for {quiet.TotalMinutes:0.#} minutes. Try again.", e);
        }
        catch (ExtractNotDownloadedException)
        {
            File.Delete(part);
            throw;
        }
        if (File.Exists(kept)) File.Move(kept, Path.Combine(folder, PreviousFileName), overwrite: true);
        File.Move(part, kept);
        return kept;
    }
}
