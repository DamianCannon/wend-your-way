using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WendYourWay.Data.Planning;

/// <summary>
/// What an exported route is called (T112; user: "I'd like the names used when exporting files to
/// be more useful than just saying Wend Your Way and the length!", chose "Its own name, else place"):
/// the route's own name, its file named from it, lower-case, letters, digits and dashes.
/// </summary>
public static partial class ExportNames
{
    /// <summary>The name, trimmed, if it says anything (a letter or a digit); else null.</summary>
    public static string? Usable(string? name) =>
        name is not null && name.Any(char.IsLetterOrDigit) ? name.Trim() : null;

    /// <summary>
    /// A file name from it, with this extension: "Kenwood House · 9.7 km" is
    /// "kenwood-house-9.7km" (a length's km kept to its number).
    /// </summary>
    public static string FileName(string name, string extension)
    {
        var lower = Km().Replace(name.ToLower(CultureInfo.InvariantCulture), "$1km");
        var kept = new StringBuilder(lower.Length);
        foreach (var c in lower) kept.Append(char.IsLetterOrDigit(c) || c == '.' ? c : '-');
        var file = Dashes().Replace(kept.ToString(), "-").Trim('-', '.');
        return (file.Length == 0 ? "cutthrough" : file) + "." + extension;
    }

    [GeneratedRegex(@"(\d)\s*km\b")]
    private static partial Regex Km();

    [GeneratedRegex("-+")]
    private static partial Regex Dashes();
}
