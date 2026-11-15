using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>What loading the Definitive Map byways did (T021).</summary>
/// <param name="Authorities">Highway authorities whose byway file was read.</param>
/// <param name="Byways">Definitive Map byways loaded.</param>
/// <param name="LengthM">Their length.</param>
/// <param name="WithoutData">Authorities whose Definitive Map isn't open, by their boundary.</param>
public sealed record RightsOfWayBuild(long Authorities, long Byways, double LengthM, long WithoutData = 0);

/// <summary>
/// The build's first phase for Trail Rides (PLAN T021; SPEC R9.6): the highway authorities'
/// Definitive Map byways, rowmaps.com's GeoJSON of their open data, one folder per authority
/// (<c>WT/mutated4.json</c>), loaded in place of any loaded before, so the Segments can be matched
/// to them.
/// </summary>
public static class RightsOfWayPhase
{
    /// <summary>Each authority's byways: rowmaps.com's file for byways open to all traffic.</summary>
    public const string BywayFile = "mutated4.json";

    /// <summary>
    /// The folder of boundaries, one KML per authority whose Definitive Map isn't open
    /// (rowmaps.com's realbounds), where OSM's byways are Unconfirmed; with names.tsv, each code's
    /// authority, since a file can hold a neighbour too (Brent's holds Brentwood, which is open).
    /// </summary>
    public const string WithoutDataFolder = "without-data";

    /// <summary>Where to get them, for a build that hasn't.</summary>
    public const string Source = "https://www.rowmaps.com/jsons/<code>/mutated4.json";

    /// <summary>Loads the byways under <paramref name="folder"/> in place of those loaded before.</summary>
    public static async Task<RightsOfWayBuild> Load(NpgsqlConnection connection, NpgsqlTransaction transaction, string folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using (var command = new NpgsqlCommand("truncate rights_of_way.byways", connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        long authorities = 0;
        foreach (var file in Directory.EnumerateFiles(folder, BywayFile, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var authority = Path.GetFileName(Path.GetDirectoryName(file))!;
            var lines = new List<string>();
            await using (var stream = File.OpenRead(file))
            using (var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken))
            {
                foreach (var feature in json.RootElement.GetProperty("features").EnumerateArray())
                {
                    if (feature.TryGetProperty("geometry", out var geometry) && geometry.ValueKind == JsonValueKind.Object
                        && geometry.GetProperty("type").GetString() == "LineString")
                    {
                        lines.Add(geometry.GetRawText());
                    }
                }
            }
            authorities++;
            if (lines.Count == 0) continue;

            await using var insert = new NpgsqlCommand("""
                insert into rights_of_way.byways (authority, geom)
                select @authority, g
                  from (select ST_Transform(ST_SetSRID(ST_GeomFromGeoJSON(line), 4326), 27700) as g from unnest(@lines) as line) l
                 where ST_NPoints(g) >= 2 and ST_Length(g) > 0
                """, connection, transaction);
            insert.Parameters.AddWithValue("authority", authority);
            insert.Parameters.AddWithValue("lines", lines.ToArray());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var withoutData = await LoadWithoutData(connection, transaction, Path.Combine(folder, WithoutDataFolder), cancellationToken);

        await using (var command = new NpgsqlCommand("analyze rights_of_way.byways; analyze rights_of_way.without_data", connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var command = new NpgsqlCommand("select count(*), coalesce(sum(ST_Length(geom)), 0) from rights_of_way.byways", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            return new RightsOfWayBuild(authorities, reader.GetInt64(0), reader.GetDouble(1), withoutData);
        }
    }

    // Each KML's polygons, joined into the authority's area, in place of those loaded before.
    private static async Task<long> LoadWithoutData(NpgsqlConnection connection, NpgsqlTransaction transaction, string folder, CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand("truncate rights_of_way.without_data", connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!Directory.Exists(folder)) return 0;

        var names = File.Exists(Path.Combine(folder, "names.tsv"))
            ? (await File.ReadAllLinesAsync(Path.Combine(folder, "names.tsv"), cancellationToken))
                .Select(line => line.Split('\t')).Where(f => f.Length == 2).ToDictionary(f => f[0], f => f[1])
            : [];

        long loaded = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*.kml").Order(StringComparer.Ordinal))
        {
            var kml = await File.ReadAllTextAsync(file, cancellationToken);
            var code = Path.GetFileNameWithoutExtension(file);
            var polygons = Regex.Matches(kml, "<Placemark>.*?</Placemark>", RegexOptions.Singleline)
                .Select(m => m.Value)
                .Where(placemark => !names.TryGetValue(code, out var name) || IsNamed(placemark, name))
                .SelectMany(placemark => Regex.Matches(placemark, "<Polygon>.*?</Polygon>", RegexOptions.Singleline).Select(m => m.Value))
                .ToArray();
            if (polygons.Length == 0) continue;
            await using var insert = new NpgsqlCommand("""
                insert into rights_of_way.without_data (authority, geom)
                select @authority, ST_Multi(ST_CollectionExtract(ST_MakeValid(ST_Union(ST_Transform(ST_SetSRID(ST_Force2D(ST_GeomFromKML(p)), 4326), 27700))), 3))
                  from unnest(@polygons) as p
                """, connection, transaction);
            insert.Parameters.AddWithValue("authority", code);
            insert.Parameters.AddWithValue("polygons", polygons);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            loaded++;
        }
        return loaded;
    }

    // "Brent London Boro" is Brent; "Brentwood District (B)" isn't.
    private static bool IsNamed(string placemark, string name)
    {
        var named = Regex.Match(placemark, "<SimpleData name=\"Name\">([^<]*)</SimpleData>") is { Success: true } data
            ? data.Groups[1].Value
            : Regex.Match(placemark, "<name>([^<]*)</name>").Groups[1].Value;
        return named == name || named.StartsWith(name + " ", StringComparison.Ordinal);
    }
}
