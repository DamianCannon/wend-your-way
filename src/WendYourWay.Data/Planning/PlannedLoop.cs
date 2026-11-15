using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Data.Planning;

/// <summary>A Loop ready for the planner screen: its line in travel order, start, Anchors and Breakdown.</summary>
/// <param name="Line">Where the Loop goes, from its start round to its start again.</param>
/// <param name="Start">Where the Loop starts: the junction the clicked start snapped to.</param>
/// <param name="Anchors">The Anchors in the order the Loop passes them, for numbering.</param>
/// <param name="Profile">Its elevation profile (T051), or none without OS Terrain 50's tiles.</param>
public sealed record PlannedLoop(IReadOnlyList<LatLon> Line, LatLon Start, IReadOnlyList<LatLon> Anchors, Breakdown Breakdown, IReadOnlyList<ProfileSample>? Profile = null)
{
    /// <summary>Its elevation profile (T051), or none without OS Terrain 50's tiles.</summary>
    public IReadOnlyList<ProfileSample> Profile { get; init; } = Profile ?? [];

    /// <summary>
    /// The OSM way of each point of <see cref="Line"/> (T077), for the piece of the line ending
    /// there; 0 across Open ground. None where not known.
    /// </summary>
    public IReadOnlyList<long> Ways { get; init; } = [];

    /// <summary>Where each of its Avoids is (T079), in the order they were made, to mark them.</summary>
    public IReadOnlyList<LatLon> Avoids { get; init; } = [];

    /// <summary>
    /// An imported route's gaps (T083): each stretch of the GPX that couldn't be matched onto the
    /// Network, along the GPX's own points.
    /// </summary>
    public IReadOnlyList<RouteGap> Gaps { get; init; } = [];

    /// <summary>
    /// Whether it was offered in another direction, below the usual bar, because few Loops were on it
    /// (T096, STORY-044). Of the Request that offered it, so not kept with a Saved Route.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool InAnotherDirection { get; init; }

    /// <summary>
    /// A line ending this near its start is a Loop (T083, T092; T121: the one rule for a route
    /// planned, imported or recorded).
    /// </summary>
    public const double ClosedWithinM = 200;

    /// <summary>
    /// Whether its line ends back where it began (T092): within <see cref="ClosedWithinM"/>, a Loop;
    /// otherwise a route from one place to another. Its line, not its start, which may be off the
    /// Network.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EndsAtStart => Line.Count == 0 || Line[^1].MetresTo(Line[0]) <= ClosedWithinM;
}

/// <summary>An imported route's own points (T083), on the map and on the grid, one for one.</summary>
public sealed record GpxPoints(IReadOnlyList<LatLon> Points, IReadOnlyList<NodePoint> Grid);

/// <summary>A stretch of an imported route not on the Network (T083): its line, and how long it is.</summary>
public sealed record RouteGap(IReadOnlyList<LatLon> Line, double LengthM);

/// <summary>A point on a Loop's elevation profile: how far round, how high, and where on the map.</summary>
public sealed record ProfileSample(double DistanceM, double HeightM, LatLon At);
