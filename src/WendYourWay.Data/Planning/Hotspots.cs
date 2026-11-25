using WendYourWay.Core.Loops;

namespace WendYourWay.Data.Planning;

/// <summary>
/// A Hotspot (STORY-039): somewhere across the country worth going to for an Activity, marked at <paramref name="At"/>
/// with its km, the box round it, and the nearest town or village, if known. A Gravel Ride's are Gravel Off-road
/// networks (T072); a Trail Ride's, its byways near one another (T189); a Run's, where off-road ground is densest (T190).
/// </summary>
public sealed record Hotspot(string? Near, LatLon At, double Km, MapBounds Box);

/// <summary>The Hotspots of each Activity (T188, #192): only the chosen Activity's are shown.</summary>
public interface IHotspots
{
    /// <summary>Every Hotspot of <paramref name="activity"/>, largest first; none where its kind isn't built.</summary>
    Task<IReadOnlyList<Hotspot>> OfAsync(Activity activity, CancellationToken cancellationToken = default);
}

/// <summary>Each Activity's Hotspots, read from the Network: a Gravel Ride's its Gravel hotspots.</summary>
public sealed class NetworkHotspots(IGravelHotspots gravel) : IHotspots
{
    public async Task<IReadOnlyList<Hotspot>> OfAsync(Activity activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gravel);
        return activity switch
        {
            Activity.GravelRide => await gravel.AllAsync(cancellationToken: cancellationToken),
            _ => [],
        };
    }

    /// <summary>An Activity as the map and its requests name it (T188): "run", "gravel-ride", "trail-ride".</summary>
    public static string Named(Activity activity) => activity switch
    {
        Activity.GravelRide => "gravel-ride",
        Activity.TrailRide => "trail-ride",
        _ => "run",
    };

    /// <summary>The Activity named so (T188); null for any other name.</summary>
    public static Activity? Called(string? name) => name switch
    {
        "run" => Activity.Run,
        "gravel-ride" => Activity.GravelRide,
        "trail-ride" => Activity.TrailRide,
        _ => null,
    };
}
