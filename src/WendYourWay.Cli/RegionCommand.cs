using WendYourWay.Data.Building;

namespace WendYourWay.Cli;

/// <summary>
/// `wend network regions` and `wend network build --region` (#147, T143, STORY-046 AC2): the regions of
/// England and Wales Geofabrik publishes, and a Network built from one of them, its extract downloaded
/// (checked against its MD5) into the data folder first.
/// </summary>
public static class RegionCommand
{
    // Over IPv4 first, and tried again (T154).
    private static readonly HttpClient Http = GeofabrikConnection.Client("WendYourWay-region/1.0");

    public static async Task<int> List(string index, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var region in await new GeofabrikRegions(Http, index).InEnglandAndWalesAsync(cancellationToken))
            await output.WriteLineAsync($"  {region.Id,-28}{region.Name}");
        return 0;
    }

    /// <summary>The region's extract, downloaded into <paramref name="dataFolder"/>; null, said why, if it can't be.</summary>
    public static async Task<string?> DownloadAsync(string wanted, string index, string dataFolder, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var regions = new GeofabrikRegions(Http, index);
        var (found, inEnglandAndWales) = await regions.FindAsync(wanted, cancellationToken);
        if (found is null)
        {
            var closest = GeofabrikRegions.Closest(await regions.InEnglandAndWalesAsync(cancellationToken), wanted);
            await output.WriteLineAsync($"No region called '{wanted}' in England or Wales.{(closest is null ? "" : $" Did you mean {closest}?")} wend network regions lists them all.");
            return null;
        }
        if (!inEnglandAndWales)
        {
            await output.WriteLineAsync($"{found.Name} isn't in England or Wales: the routes are planned for England and Wales only.");
            return null;
        }
        await output.WriteLineAsync($"Downloading {found.Name}'s map from Geofabrik");
        var source = new ExtractSource(Http, found.Extract);
        var extract = await source.DownloadAsync(dataFolder, cancellationToken: cancellationToken);
        await output.WriteLineAsync($"  {new FileInfo(extract).Length:N0} bytes, matching its MD5");
        if (source.TookEarlier is { } day) await output.WriteLineAsync(ExtractSource.Earlier(day));
        return extract;
    }
}
