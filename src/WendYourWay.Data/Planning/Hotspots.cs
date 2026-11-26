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

/// <summary>
/// Each Activity's Hotspots, read from the Network: a Gravel Ride's its Gravel hotspots (T072); a Trail Ride's its
/// byways near one another (T189).
/// </summary>
public sealed class NetworkHotspots(IGravelHotspots gravel, Npgsql.NpgsqlDataSource dataSource) : IHotspots
{
    /// <summary>
    /// A Trail Ride hotspot's least size (T189): the spike (T187) found 53 of 10 km or more across England and Wales,
    /// 25 of 20 km. Kept from 5 km, so this can move without a build.
    /// </summary>
    public const double TrailRideFromKm = 10;

    /// <summary>
    /// A Run hotspot's least density (T190): the km of off-road ground a Run may use in one of the build's 5 km squares
    /// for it to count. The spike (T187) found 163 squares of 100 km or more in England and Wales (the top 2.5%; the
    /// median, 36 km). Lowered to 75 km (T193, #197; user: "a little sparse when cut off at 100km"): on the real map,
    /// 196 hotspots in place of 90, the largest 46 squares; at 50 km, one ran to 318. Read as they're shown, so this can
    /// move without a build.
    /// </summary>
    public const double RunFromKm = 75;

    public Task<IReadOnlyList<Hotspot>> OfAsync(Activity activity, CancellationToken cancellationToken = default) =>
        OfAsync(activity, null, cancellationToken);

    /// <summary>Those of <paramref name="fromKm"/> or more, else of the Activity's own least size.</summary>
    public async Task<IReadOnlyList<Hotspot>> OfAsync(Activity activity, double? fromKm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gravel);
        return activity switch
        {
            Activity.GravelRide => await gravel.AllAsync(fromKm ?? PostgisGravelHotspots.FromKm, cancellationToken),
            Activity.TrailRide => await HotspotTable.ReadAsync(dataSource, "trail_networks", fromKm ?? TrailRideFromKm, cancellationToken),
            // A Run's least size is a square's density, not an area's km (T190).
            _ => await RunSquares.ReadAsync(dataSource, fromKm ?? RunFromKm, cancellationToken),
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
