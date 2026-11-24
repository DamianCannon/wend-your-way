using System.Globalization;
using System.IO.Compression;
using System.Text;
using WendYourWay.Core.Network;
using WendYourWay.Core.Places;

namespace WendYourWay.Data.Places;

/// <summary>
/// An OS Open Names entry kept for search (PLAN T064): its name, its kind, the borough, district,
/// county or place that tells it apart, and where it is on the National Grid.
/// </summary>
public sealed record OpenNamesEntry(string Name, PlaceKind Kind, string? Context, NodePoint At);

/// <summary>
/// Reads OS Open Names' CSV files (its Data folder: one per 10 km square, no header, a BOM), and
/// keeps England's and Wales's places, postcodes and green areas; a Welsh name as an entry of its
/// own. Read as it goes, never all at once: 1.7 GB for Great Britain.
/// </summary>
public static class OpenNames
{
    // The columns used, as Doc/OS_Open_Names_Header.csv numbers them from 0.
    private const int Name1 = 2, Name2 = 4, LocalType = 7, X = 8, Y = 9;
    private const int PopulatedPlace = 18, DistrictBorough = 21, CountyUnitary = 24, Country = 29;

    /// <summary>The CSV files of an OS Open Names download: its Data folder, or the folder above it.</summary>
    public static IReadOnlyList<string> Files(string folder)
    {
        var data = Path.Combine(folder, "Data");
        var from = Directory.Exists(data) ? data : folder;
        return Directory.Exists(from) ? [.. Directory.EnumerateFiles(from, "*.csv").Order(StringComparer.OrdinalIgnoreCase)] : [];
    }

    /// <summary>The entries of one file kept for search.</summary>
    public static IEnumerable<OpenNamesEntry> Read(string file) => Read(() => File.OpenRead(file));

    /// <summary>The CSV files in OS's zip of OS Open Names (T171): its Data folder's, in order.</summary>
    public static IReadOnlyList<ZipArchiveEntry> Files(ZipArchive zip)
    {
        ArgumentNullException.ThrowIfNull(zip);
        return [.. zip.Entries.Where(e => e.FullName.Replace('\\', '/').Contains("Data/", StringComparison.OrdinalIgnoreCase)
                && e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The entries of one file in OS's zip kept for search (T171).</summary>
    public static IEnumerable<OpenNamesEntry> Read(ZipArchiveEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Read(file.Open);
    }

    private static IEnumerable<OpenNamesEntry> Read(Func<Stream> open)
    {
        using var reader = new StreamReader(open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        foreach (var row in Csv.Rows(reader))
        {
            if (row.Count <= Country || row[Country] is not ("England" or "Wales")) continue;
            if (PlaceSearch.KindOf(row[LocalType]) is not { } kind) continue;
            var at = new NodePoint(double.Parse(row[X], CultureInfo.InvariantCulture), double.Parse(row[Y], CultureInfo.InvariantCulture));
            var context = kind == PlaceKind.Postcode
                ? Either(row[PopulatedPlace], row[DistrictBorough], row[CountyUnitary])
                : Either(row[DistrictBorough], row[CountyUnitary]);
            yield return new OpenNamesEntry(row[Name1], kind, context, at);
            if (kind != PlaceKind.Postcode && row[Name2].Length > 0) yield return new OpenNamesEntry(row[Name2], kind, context, at);
        }
    }

    private static string? Either(params string[] values) => values.FirstOrDefault(v => v.Length > 0);
}

/// <summary>RFC 4180 CSV: a field in quotes may hold commas, and a doubled quote is a quote.</summary>
internal static class Csv
{
    public static IEnumerable<List<string>> Rows(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        int c;
        while ((c = reader.Read()) != -1)
        {
            var ch = (char)c;
            if (quoted)
            {
                if (ch != '"') field.Append(ch);
                else if (reader.Peek() == '"') field.Append((char)reader.Read());
                else quoted = false;
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                yield return row;
                row = [];
            }
            else if (ch != '\r') field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
