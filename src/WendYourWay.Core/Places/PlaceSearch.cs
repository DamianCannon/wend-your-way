using System.Text;
using System.Text.RegularExpressions;

namespace WendYourWay.Core.Places;

/// <summary>What a place found by search is (PLAN T064), in the order they're offered when equally good matches.</summary>
public enum PlaceKind
{
    City,
    Town,
    Suburb,
    Settlement,
    Village,
    /// <summary>A park, common, nature reserve, wood, forest or heath named on the map itself.</summary>
    Park,
    Wood,
    Greenspace,
    Hill,
    Water,
    Landcover,
    Valley,
    Station,
    Hamlet,
    /// <summary>A postcode district, "EN4", made from its postcodes.</summary>
    District,
    Postcode,
}

/// <summary>A place that matched a search (PLAN T064): its key, its kind, and how far it is from the middle of the map.</summary>
public sealed record PlaceCandidate(int Id, string Key, PlaceKind Kind, double FromMiddleM);

/// <summary>
/// Search by name and postcode (PLAN T064): "I want to be able to search by name and postcode"
/// (user, 2026-11-03), to take the map to a new area.
/// </summary>
public static partial class PlaceSearch
{
    /// <summary>How many places are shown.</summary>
    public const int Shown = 8;

    /// <summary>How many characters are typed before searching.</summary>
    public const int FromCharacters = 2;

    /// <summary>A name as it's searched: lower case, without accents or punctuation, one space between words.</summary>
    public static string Key(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = new StringBuilder(name.Length);
        var space = false;
        foreach (var c in name)
        {
            // An apostrophe joins: King's Lynn is "kings lynn".
            if (c is '\'' or '’') continue;
            if (char.IsLetterOrDigit(c))
            {
                if (space && key.Length > 0) key.Append(' ');
                key.Append(Unaccented(char.ToLowerInvariant(c)));
                space = false;
            }
            else space = true;
        }
        return key.ToString();
    }

    // The accented letters of English and Welsh names, by hand: the app runs without ICU, so
    // Unicode normalisation can't take them off.
    private const string Accented = "àáâãäåāèéêëēìíîïīòóôõöøōùúûüūŵýÿŷçñ";
    private const string Plain = "aaaaaaaeeeeeiiiiiooooooouuuuuwyyycn";

    private static char Unaccented(char c) => Accented.IndexOf(c, StringComparison.Ordinal) is >= 0 and var i ? Plain[i] : c;

    /// <summary>A postcode, district or the start of one as it's searched, "sw1a1aa"; null if what's typed isn't one.</summary>
    public static string? PostcodeKey(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var key = Key(text).Replace(" ", "", StringComparison.Ordinal);
        return PostcodeShape().IsMatch(key) ? key : null;
    }

    // An outward code (a letter or two, a digit, then a letter or digit perhaps), and perhaps the
    // start of an inward code (a digit, then up to two letters).
    [GeneratedRegex("^[a-z]{1,2}[0-9][a-z0-9]?([0-9][a-z]{0,2})?$")]
    private static partial Regex PostcodeShape();

    /// <summary>The kind of an OS Open Names entry, by its local type; null for those left out (roads, schools and the rest).</summary>
    public static PlaceKind? KindOf(string localType) => localType switch
    {
        "City" => PlaceKind.City,
        "Town" => PlaceKind.Town,
        "Suburban Area" => PlaceKind.Suburb,
        "Other Settlement" => PlaceKind.Settlement,
        "Village" => PlaceKind.Village,
        "Hamlet" => PlaceKind.Hamlet,
        "Woodland Or Forest" => PlaceKind.Wood,
        "Urban Greenspace" => PlaceKind.Greenspace,
        "Other Landcover" => PlaceKind.Landcover,
        "Hill Or Mountain" or "Hill Or Mountain Ranges" => PlaceKind.Hill,
        "Valley" => PlaceKind.Valley,
        "Inland Water" => PlaceKind.Water,
        "Railway Station" => PlaceKind.Station,
        "Postcode" => PlaceKind.Postcode,
        _ => null,
    };

    /// <summary>The zoom the map shows a place of this kind at: never out past the car parks'.</summary>
    public static int ZoomFor(PlaceKind kind) => kind switch
    {
        PlaceKind.City => 12,
        PlaceKind.Town => 13,
        PlaceKind.Hamlet or PlaceKind.Station => 15,
        PlaceKind.Postcode => 16,
        _ => 14,
    };

    /// <summary>
    /// The places to show for <paramref name="key"/>: the whole name, then its start, then a later
    /// word's start; then by <see cref="Tier"/>; then nearest the middle of the map; then by kind.
    /// <see cref="Shown"/> of them.
    /// </summary>
    public static IReadOnlyList<PlaceCandidate> Rank(IEnumerable<PlaceCandidate> candidates, string key)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return [.. candidates
            .Select(c => (Candidate: c, Match: Match(c.Key, key)))
            .Where(m => m.Match < NoMatch)
            .OrderBy(m => m.Match)
            .ThenBy(m => Tier(m.Candidate.Kind))
            .ThenBy(m => m.Candidate.FromMiddleM)
            .ThenBy(m => m.Candidate.Kind)
            .ThenBy(m => m.Candidate.Id)
            .Take(Shown)
            .Select(m => m.Candidate)];
    }

    /// <summary>
    /// How a kind ranks among places of one name: cities and towns first, wherever they are; then
    /// suburbs, villages, green areas, hills, water and stations together, the nearest first ("richmond
    /// park" from London found a suburb by Rotherham before the park, 2026-11-03); then hamlets;
    /// then postcode districts and postcodes.
    /// </summary>
    public static int Tier(PlaceKind kind) => kind switch
    {
        PlaceKind.City or PlaceKind.Town => 0,
        PlaceKind.Hamlet => 2,
        PlaceKind.District => 3,
        PlaceKind.Postcode => 4,
        _ => 1,
    };

    private const int NoMatch = 3;

    private static int Match(string name, string key) =>
        name == key ? 0
        : name.StartsWith(key, StringComparison.Ordinal) ? 1
        : name.Contains(" " + key, StringComparison.Ordinal) ? 2
        : NoMatch;
}
