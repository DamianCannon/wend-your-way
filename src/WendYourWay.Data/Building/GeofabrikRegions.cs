using System.Net.Http.Json;
using System.Text.Json;

namespace WendYourWay.Data.Building;

/// <summary>A region Geofabrik publishes an extract of: its id (as in its addresses), its name, and its extract's address.</summary>
public sealed record Region(string Id, string Name, string Extract);

/// <summary>
/// The regions to build a Network of (#147, T143, STORY-046 AC2; user: "how other people can use my
/// github repository to set this up for themselves"): those in Geofabrik's index of its extracts that lie
/// in England or Wales, the app's ground (England and Wales themselves, their counties).
/// </summary>
public sealed class GeofabrikRegions(HttpClient http, string indexUrl)
{
    /// <summary>Geofabrik's index of every extract it publishes, without their outlines.</summary>
    public const string Index = "https://download.geofabrik.de/index-v1-nogeom.json";

    private static readonly string[] Ours = ["england", "wales"];

    private List<(Region Region, string? Parent)>? _all;

    /// <summary>Every region in England or Wales, by id.</summary>
    public async Task<IReadOnlyList<Region>> InEnglandAndWalesAsync(CancellationToken cancellationToken = default)
    {
        var all = await AllAsync(cancellationToken);
        var parents = all.ToDictionary(r => r.Region.Id, r => r.Parent);
        return [.. all.Where(r => Within(r.Region.Id, parents)).Select(r => r.Region).OrderBy(r => r.Id, StringComparer.Ordinal)];
    }

    /// <summary>The region by its id or its name, written any way (case, spaces); none if there's no such region anywhere.</summary>
    public async Task<(Region? Found, bool InEnglandAndWales)> FindAsync(string wanted, CancellationToken cancellationToken = default)
    {
        var all = await AllAsync(cancellationToken);
        var key = Key(wanted);
        if (all.FirstOrDefault(r => Key(r.Region.Id) == key || Key(r.Region.Name) == key) is not { Region: not null } found) return (null, false);
        return (found.Region, Within(found.Region.Id, all.ToDictionary(r => r.Region.Id, r => r.Parent)));
    }

    /// <summary>The region in England or Wales whose id is closest to what was written, if one is close (two letters out at most).</summary>
    public static string? Closest(IEnumerable<Region> regions, string wanted) =>
        regions.Select(r => (r.Id, Distance: Distance(Key(r.Id), Key(wanted)))).Where(r => r.Distance <= 2).OrderBy(r => r.Distance).Select(r => r.Id).FirstOrDefault();

    private async Task<List<(Region Region, string? Parent)>> AllAsync(CancellationToken cancellationToken)
    {
        if (_all is not null) return _all;
        using var index = await http.GetFromJsonAsync<JsonDocument>(indexUrl, cancellationToken)
            ?? throw new InvalidOperationException($"Geofabrik's index at {indexUrl} is empty.");
        _all = [];
        foreach (var feature in index.RootElement.GetProperty("features").EnumerateArray())
        {
            var properties = feature.GetProperty("properties");
            if (!properties.TryGetProperty("urls", out var urls) || !urls.TryGetProperty("pbf", out var pbf)) continue;
            _all.Add((new Region(properties.GetProperty("id").GetString()!, properties.GetProperty("name").GetString()!, pbf.GetString()!),
                properties.TryGetProperty("parent", out var parent) ? parent.GetString() : null));
        }
        return _all;
    }

    // England, Wales, or under one of them.
    private static bool Within(string id, Dictionary<string, string?> parents)
    {
        for (string? at = id, guard = null; at is not null && at != guard; guard = at, at = parents.GetValueOrDefault(at))
            if (Ours.Contains(at)) return true;
        return false;
    }

    private static string Key(string name) => name.Trim().ToLowerInvariant().Replace(' ', '-');

    // Letters to change, add or remove to get from one to the other.
    private static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var (diagonal, row0) = (row[0], i);
            row[0] = row0;
            for (var j = 1; j <= b.Length; j++)
            {
                var next = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                (diagonal, row[j]) = (row[j], next);
            }
        }
        return row[b.Length];
    }
}
