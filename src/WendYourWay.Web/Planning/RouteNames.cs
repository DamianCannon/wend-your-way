using System.Globalization;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Planning;

/// <summary>
/// What the planner calls a start, a finish and a route (T080, T093; moved out of the page, T123):
/// the Start and Finish boxes' words and the name Save offers.
/// </summary>
public static class RouteNames
{
    /// <summary>What a start or finish clicked on the map, with no postcode near, is called.</summary>
    public const string ChosenOnTheMap = "Chosen on the map";

    /// <summary>
    /// The Start box's words: nothing with no start; "Home" at home; a car park's name at the car park
    /// chosen; else its postcode, or <see cref="ChosenOnTheMap"/> with none.
    /// </summary>
    public static string Start(LatLon? start, LatLon? home, FoundStart? chosen, string? startName, string homeName = "Home") =>
        start is null ? ""
        : start == home ? homeName
        : chosen is { } carPark && start == carPark.At ? carPark.Name
        : startName ?? ChosenOnTheMap;

    /// <summary>The Finish box's words: empty for a Loop, or till a finish is chosen; else its name.</summary>
    public static string Finish(bool endElsewhere, LatLon? finish, string? finishName) =>
        endElsewhere && finish is not null ? finishName ?? ChosenOnTheMap : "";

    /// <summary>
    /// The name Save offers for the route <paramref name="shown"/> (T080): an imported route's own;
    /// else where it starts, and to where for one <paramref name="toAFinish"/>, and its km.
    /// </summary>
    public static string Suggested(string? imported, PlannedLoop? shown, bool toAFinish, string start, string finish) =>
        imported is { } name ? name
        : shown is null ? ""
        : toAFinish ? string.Create(CultureInfo.InvariantCulture, $"{start} to {finish} · {shown.Breakdown.LengthM / 1000:0.0} km")
        : string.Create(CultureInfo.InvariantCulture, $"{start} · {shown.Breakdown.LengthM / 1000:0.0} km");
}

/// <summary>
/// The names Loops were saved or opened by (T112; moved out of the page, T123), each Loop by itself:
/// a record compares by value, so these by reference, and another Loop paged to isn't called by one
/// saved before it. Never pruned: a session's few names.
/// </summary>
public sealed class SavedNames
{
    private readonly Dictionary<PlannedLoop, string> _names = new(ReferenceEqualityComparer.Instance);

    /// <summary>Saved by <paramref name="name"/>, trimmed as Saved Routes keeps it.</summary>
    public void Saved(PlannedLoop loop, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _names[loop] = name.Trim();
    }

    /// <summary>Opened from Saved Routes, by the name it's kept under.</summary>
    public void Opened(PlannedLoop loop, string name) => _names[loop] = name;

    /// <summary>Nudged, <paramref name="after"/> keeps the name <paramref name="before"/> had, if any.</summary>
    public void Carry(PlannedLoop before, PlannedLoop after)
    {
        if (_names.TryGetValue(before, out var name)) _names[after] = name;
    }

    /// <summary>
    /// What the route shown is exported as (user: "Its own name, else place"): the name it was saved
    /// or opened by, else <paramref name="suggested"/>, the name Save offers.
    /// </summary>
    public string ExportName(PlannedLoop? shown, string suggested) =>
        shown is not null && _names.TryGetValue(shown, out var saved) ? saved : suggested;
}
