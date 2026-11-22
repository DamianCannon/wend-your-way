using System.Diagnostics.CodeAnalysis;

namespace WendYourWay.Core.Network;

/// <summary>What a Segment is underfoot and whether traffic uses it (see CONTEXT.md).</summary>
public enum GroundKind
{
    OffRoad,
    TrafficFree,
    Connector,
}

/// <summary>Whether an Activity may lawfully use a Segment.</summary>
public enum AccessKind
{
    Allowed,
    Unconfirmed,
    PushOnly,
    Forbidden,
}

/// <summary>
/// Whether a Segment is a gap in the map (#150, T146): no way of its own, but a path's dead end joined to
/// another way it stops short of, with nothing mapped across. Joined, under 5 m: used like its path. Possible,
/// 5 m to 15 m: used only when the user includes it, so a slice reads it as forbidden.
/// </summary>
public enum MapGapKind
{
    None,
    Joined,
    Possible,
}

/// <summary>
/// How a Crossing is controlled (CONTEXT.md; T044), from its crossing way's tags: by traffic
/// signals, by markings (a zebra), or not at all. A bridge or underpass is a path, not a Crossing.
/// </summary>
public enum CrossingKind
{
    Unmarked,
    Marked,
    Signals,
}

/// <summary>
/// A Segment's identity in OSM terms, which survives Network rebuilds (ADR 0005). An Open ground
/// crossing has no OSM way: its way id is 0, and its junctions' node ids identify it (T039).
/// </summary>
public readonly record struct SegmentKey(long OsmWayId, long FromNodeId, long ToNodeId)
{
    /// <summary>Whether this is a straight crossing of Open ground rather than part of an OSM way.</summary>
    public bool IsOpenGround => OsmWayId == 0;
}

/// <summary>
/// Metres of a Segment lying on a same-ground group: ground other Segments may cover too, such
/// as a road Segment and the pavements beside it (CONTEXT.md: Retrace).
/// </summary>
public readonly record struct GroundShare(int Group, double Metres);

/// <summary>
/// The stretch of a single way between two junctions. <see cref="From"/> and <see cref="To"/>
/// are node indices within the <see cref="NetworkSlice"/> that holds it.
/// </summary>
/// <param name="Access">
/// Whether the Request's Activity may use it (CONTEXT.md: Access): a slice is loaded for one
/// Activity (T068), so the router and the Loops never ask which.
/// </param>
/// <param name="SameGround">
/// The same-ground groups the Segment lies on and for how many metres of it. A road Segment is a
/// group of its own; a pavement lies on each road Segment it runs beside, for the metres it runs
/// beside it, and on a group of its own for the rest.
/// </param>
/// <param name="MainRoad">
/// A Connector with heavy traffic (CONTEXT.md): a trunk, primary or secondary road, or a pavement
/// beside one. Used only for short links (R2.1).
/// </param>
/// <param name="Greenery">The share of it within 15 m of green areas, 0 to 1 (T036).</param>
/// <param name="Waterside">The share of it within 25 m of water, 0 to 1 (T043).</param>
/// <param name="Crossing">The kind of Crossing it's part of, if it's part of a crossing way (T044).</param>
/// <param name="CrossesMainRoad">Whether that Crossing is of a Main Road.</param>
/// <param name="CrossingShare">Its share of the Crossing by length: a crossing way's Segments share its cost.</param>
/// <param name="BridgeOverWater">Whether it's on a bridge over water, where a Loop along a river can cross back (T046).</param>
/// <param name="AscentM">Metres it climbs going from <see cref="From"/> to <see cref="To"/> (T049).</param>
/// <param name="DescentM">Metres it descends going that way: what it climbs the other way.</param>
/// <param name="Corridor">
/// The name of the Corridor it lies on (CONTEXT.md; T037), or none: green ground on a walking
/// route, or a national or international cycle route.
/// </param>
public sealed record Segment(
    SegmentKey Key,
    int From,
    int To,
    double LengthM,
    GroundKind Ground,
    AccessKind Access,
    IReadOnlyList<GroundShare> SameGround,
    bool MainRoad = false,
    double Greenery = 0,
    string? Corridor = null,
    double Waterside = 0,
    CrossingKind? Crossing = null,
    bool CrossesMainRoad = false,
    double CrossingShare = 0,
    bool BridgeOverWater = false,
    double AscentM = 0,
    double DescentM = 0)
{
    /// <summary>A Segment lying wholly on one same-ground group.</summary>
    public Segment(SegmentKey Key, int From, int To, double LengthM, GroundKind Ground, AccessKind Access, int SameGroundGroup, bool MainRoad = false, double Greenery = 0, string? Corridor = null, double Waterside = 0,
        CrossingKind? Crossing = null, bool CrossesMainRoad = false, double CrossingShare = 0, bool BridgeOverWater = false, double AscentM = 0, double DescentM = 0)
        : this(Key, From, To, LengthM, Ground, Access, [new GroundShare(SameGroundGroup, LengthM)], MainRoad, Greenery, Corridor, Waterside,
            Crossing, CrossesMainRoad, CrossingShare, BridgeOverWater, AscentM, DescentM)
    {
    }

    /// <summary>
    /// Whether it's on its Corridor: green ground only, so a pavement the build found beside a road
    /// is a road, whatever route it's on (T037).
    /// </summary>
    [MemberNotNullWhen(true, nameof(Corridor))]
    public bool OnCorridor => Corridor is not null && Ground != GroundKind.Connector;

    /// <summary>The Obstacles on it (T069), or none: most Segments have none.</summary>
    public IReadOnlyList<ObstacleShare>? Obstacles { get; init; }

    /// <summary>Whether it's a Main Road's carriageway itself (T145), not a pavement beside one: its sides are what's crossed.</summary>
    public bool Carriageway { get; init; }

    /// <summary>
    /// Whether it's a Main Road with a real pavement to walk on (T181): 30 mph or under, with pavements on
    /// both sides, or a pavement beside such a road. Only in a Run's slice: on a bike it's the road.
    /// </summary>
    public bool Pavement { get; init; }

    /// <summary>
    /// Whether its road is busy (T145): a trunk road, four lanes or more, or a dual carriageway, where a
    /// crossing with no crossing point is never chosen while there's another way. On a crossing link, its road's.
    /// </summary>
    public bool Busy { get; init; }

    /// <summary>
    /// Its direction leaving <see cref="From"/>, and leaving <see cref="To"/>, in degrees clockwise from grid
    /// north (T145), from its own line: what side of a Main Road it's on. None: the line between its ends.
    /// </summary>
    public double? FromBearing { get; init; }

    /// <inheritdoc cref="FromBearing"/>
    public double? ToBearing { get; init; }

    /// <summary>
    /// Whether it's a crossing of a Main Road at a junction (T145), from one of the junction's corners to
    /// the next, made by <see cref="Corners"/>: no OSM way of its own.
    /// </summary>
    public bool IsCrossingLink => Key.OsmWayId < 0;

    /// <summary>Whether it's a gap in the map (#150, T146), and which kind.</summary>
    public MapGapKind MapGap { get; init; }

    /// <summary>Metres of this Segment on ground already <paramref name="covered"/>, by same-ground group.</summary>
    public double RetracedMetres(IReadOnlySet<int> covered)
    {
        ArgumentNullException.ThrowIfNull(covered);
        // Indexed, not foreach: the router asks this of every Segment it looks at, and an
        // enumerator over the interface is an allocation each time (T018).
        var metres = 0.0;
        for (var i = 0; i < SameGround.Count; i++)
        {
            if (covered.Contains(SameGround[i].Group)) metres += SameGround[i].Metres;
        }
        return Math.Min(metres, LengthM);
    }

    /// <summary>Adds the same-ground groups this Segment lies on to <paramref name="covered"/>.</summary>
    public void Cover(ISet<int> covered)
    {
        ArgumentNullException.ThrowIfNull(covered);
        foreach (var share in SameGround) covered.Add(share.Group);
    }
}

/// <summary>
/// A Segment as the Network stores it: by OSM identity, with the positions of its two ends, before
/// it is numbered into a <see cref="NetworkSlice"/>. Its geometry runs from FromNodeId to ToNodeId.
/// </summary>
/// <param name="Access">Whether the Activity it was read for may use it.</param>
/// <param name="Beside">
/// The road Segments this one runs beside and for how many metres of it (a pavement beside its
/// road), or none.
/// </param>
/// <param name="MainRoad">A trunk, primary or secondary road, or a pavement beside one.</param>
/// <param name="Greenery">The share of it within 15 m of green areas, 0 to 1.</param>
/// <param name="Corridor">The name of the Corridor it lies on, or none.</param>
/// <param name="Waterside">The share of it within 25 m of water, 0 to 1.</param>
/// <param name="Crossing">The kind of Crossing it's part of, if any.</param>
/// <param name="CrossesMainRoad">Whether that Crossing is of a Main Road.</param>
/// <param name="CrossingShare">Its share of the Crossing by length.</param>
/// <param name="BridgeOverWater">Whether it's on a bridge over water.</param>
/// <param name="AscentM">Metres it climbs from FromNodeId to ToNodeId.</param>
/// <param name="DescentM">Metres it descends that way.</param>
public sealed record StoredSegment(
    SegmentKey Key,
    NodePoint FromPoint,
    NodePoint ToPoint,
    double LengthM,
    GroundKind Ground,
    AccessKind Access,
    IReadOnlyList<RoadShare>? Beside = null,
    bool MainRoad = false,
    double Greenery = 0,
    string? Corridor = null,
    double Waterside = 0,
    CrossingKind? Crossing = null,
    bool CrossesMainRoad = false,
    double CrossingShare = 0,
    bool BridgeOverWater = false,
    double AscentM = 0,
    double DescentM = 0)
{
    /// <summary>The Obstacles on it (T069), or none.</summary>
    public IReadOnlyList<ObstacleShare>? Obstacles { get; init; }

    /// <summary>Whether it's a Main Road's carriageway itself (T145).</summary>
    public bool Carriageway { get; init; }

    /// <summary>Whether it's a Main Road with a real pavement (T181).</summary>
    public bool Pavement { get; init; }

    /// <summary>Whether its road is busy (T145).</summary>
    public bool Busy { get; init; }

    /// <summary>Which way it leaves FromNodeId, and ToNodeId, in degrees clockwise from grid north (T145c); none if not known.</summary>
    public double? FromBearing { get; init; }

    /// <inheritdoc cref="FromBearing"/>
    public double? ToBearing { get; init; }

    /// <summary>Whether it's a gap in the map (#150, T146), and which kind.</summary>
    public MapGapKind MapGap { get; init; }
}

/// <summary>Metres of a stored Segment that run beside a road Segment.</summary>
public readonly record struct RoadShare(SegmentKey Road, double Metres);

/// <summary>A junction's position in British National Grid metres (EPSG:27700).</summary>
public readonly record struct NodePoint(double X, double Y)
{
    public double DistanceTo(NodePoint other) => Math.Sqrt(((X - other.X) * (X - other.X)) + ((Y - other.Y) * (Y - other.Y)));
}
