namespace WendYourWay.Data.Building;

/// <summary>
/// A map update's steps, in order, counted for the job line (#152, T148): "step 12 of 17, working out the
/// climbing". Each of the build's phases is a step of its own, keyed as the build names it, so the count
/// moves every ten minutes to an hour, not once in three hours; one that doesn't apply this time isn't counted.
/// </summary>
public sealed class UpdateSteps
{
    private static readonly (string Key, string Words)[] All =
    [
        ("checking", "checking for a newer map"),
        ("downloading", "downloading the map"),
        ("heights", "checking OS's heights"),
        ("osm2pgsql", "loading the map"),
        ("rights of way", "loading the rights of way"),
        ("segments", "building the paths"),
        ("same ground", "finding paths beside roads"),
        ("open ground", "crossing open ground"),
        ("off-road networks", "joining off-road networks"),
        ("trailheads", "finding Trailheads"),
        ("obstacles", "finding Obstacles"),
        ("gravel networks", "joining gravel networks"),
        ("trail ride hotspots", "finding Trail Ride hotspots"),
        ("run hotspots", "finding Run hotspots"),
        ("climbing", "working out the climbing"),
        ("in order of place", "putting the paths in order"),
        ("outings", "finding what your Outings covered"),
        ("place names", "loading OS's place names"),
        ("moving Saved Routes", "moving Saved Routes onto the new map"),
        ("calibrating", "calibrating the stars"),
        ("backing up", "backing up"),
    ];

    private readonly (string Key, string Words)[] _steps;

    private UpdateSteps((string Key, string Words)[] steps) => _steps = steps;

    /// <summary>An update's steps: OS's heights and the climbing among them only if there's terrain to climb on (T171).</summary>
    public static UpdateSteps For(bool climbing) => new([.. All.Where(s => climbing || s.Key is not ("climbing" or "heights"))]);

    /// <summary>The step, as the job line says it; null if it isn't one of this update's.</summary>
    public string? Said(string step)
    {
        var at = Array.FindIndex(_steps, s => s.Key == step);
        return at < 0 ? null : $"step {at + 1} of {_steps.Length}, {_steps[at].Words}";
    }

    /// <summary>The step that follows a phase done; null after the last, or after what isn't a step.</summary>
    public string? After(string phase)
    {
        var at = Array.FindIndex(_steps, s => s.Key == phase);
        return at < 0 || at + 1 >= _steps.Length ? null : _steps[at + 1].Key;
    }
}
