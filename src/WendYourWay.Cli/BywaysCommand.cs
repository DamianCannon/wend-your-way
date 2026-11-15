using System.Globalization;
using System.Text.RegularExpressions;
using WendYourWay.Data.Building;

namespace WendYourWay.Cli;

/// <summary>
/// `wend byways get` (#146, T142, STORY-046): the highway authorities' Definitive Map byways, for Trail
/// Rides (T021), from rowmaps.com's GeoJSON of each authority's open data (Open Government Licence;
/// contains OS data © Crown copyright and database right), the byways open to all traffic only (about
/// 8 MB for England and Wales), into &lt;folder&gt;/&lt;code&gt;/mutated4.json, which `wend network build`
/// reads. And the boundaries of the authorities whose Definitive Map isn't open (rowmaps.com's
/// realbounds, from OS Boundary-Line), with their names, into &lt;folder&gt;/without-data, where OSM's
/// byways are flagged "check this". Run again to refresh: a file the same size as rowmaps.com's is
/// skipped. Was scripts/get-rights-of-way.ps1, now any machine's (a Docker container's too).
/// </summary>
public static partial class BywaysCommand
{
    public const string Source = "https://www.rowmaps.com";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2), DefaultRequestHeaders = { { "User-Agent", "WendYourWay-byways/1.0" } } };

    public static async Task<int> Get(string folder, string source, TimeSpan pause, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var index = await Http.GetStringAsync(new Uri($"{source}/jsons/"), cancellationToken);
        var codes = Authority().Matches(index).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToList();
        if (codes.Count == 0)
        {
            await output.WriteLineAsync($"No authorities found at {source}/jsons/: has the page changed?");
            return 1;
        }

        var fetched = 0;
        foreach (var code in codes)
        {
            var url = new Uri($"{source}/jsons/{code}/{RightsOfWayPhase.BywayFile}");
            var file = Path.Combine(folder, code, RightsOfWayPhase.BywayFile);
            using (var head = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), cancellationToken))
                if (File.Exists(file) && head.Content.Headers.ContentLength == new FileInfo(file).Length) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync(url, cancellationToken), cancellationToken);
            fetched++;
            await Task.Delay(pause, cancellationToken);
        }
        await output.WriteLineAsync(string.Format(CultureInfo.InvariantCulture, "{0} authorities; {1} downloaded, the rest already here, in {2}", codes.Count, fetched, folder));

        // A file may hold a neighbour too (Brent's holds Brentwood): names.tsv says which authority each is.
        var missing = await Http.GetStringAsync(new Uri($"{source}/missing/"), cancellationToken);
        var text = Spaces().Replace(Tags().Replace(missing, " ").Replace("&nbsp;", " ", StringComparison.Ordinal), " ");
        var without = Without().Matches(text).Select(m => (Code: m.Groups[1].Value, Name: m.Groups[2].Value)).ToList();
        if (without.Count == 0)
        {
            await output.WriteLineAsync($"No authorities found at {source}/missing/: has the page changed?");
            return 1;
        }
        var boundaries = Path.Combine(folder, "without-data");
        Directory.CreateDirectory(boundaries);
        foreach (var (code, _) in without)
        {
            await File.WriteAllBytesAsync(Path.Combine(boundaries, $"{code}.kml"), await Http.GetByteArrayAsync(new Uri($"{source}/realbounds/{code}.kml"), cancellationToken), cancellationToken);
            await Task.Delay(pause, cancellationToken);
        }
        await File.WriteAllTextAsync(Path.Combine(boundaries, "names.tsv"), string.Join('\n', without.Select(w => $"{w.Code}\t{w.Name}")), cancellationToken);
        await output.WriteLineAsync($"{without.Count} authorities without open data: their boundaries in {boundaries}");
        return 0;
    }

    [GeneratedRegex("href=\"([A-Z0-9]{2})/\"")]
    private static partial Regex Authority();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\d+ ([A-Z]{2}) (.+?) KML giving")]
    private static partial Regex Without();
}
