using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Loops;

/// <summary>
/// Where an Anchor goes for a point on the generator's circle: a junction in a large Off-road
/// network nearby, when there is one, else the nearest junction. At the nearest junction an Anchor
/// could land among roads, and the Loop then goes out of its way along them to touch it; on any
/// green junction, it could land on a scrap of open space while a wood beside it went half used
/// (user, 2026-10-19). The start of increment 2's Corridor-anchored Loops.
/// </summary>
public sealed class AnchorPlacement
{
    /// <summary>How far an Anchor may move from its point to land in a large Off-road network.</summary>
    public const double GreenReachM = 300;

    private readonly NetworkSlice _slice;
    private readonly int[] _candidates;
    private readonly int[] _green;

    /// <param name="candidates">The junctions an Anchor may be placed at.</param>
    public AnchorPlacement(NetworkSlice slice, int[] candidates)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(candidates);
        _slice = slice;
        _candidates = candidates;
        // A large Off-road network: one that could hold a stretch of Flow.
        var networks = slice.OffRoadNetworks;
        _green = [.. candidates.Where(j => OnGreen(j) && networks.MetresAt(j) >= Flow.LongStretchM)];
    }

    /// <summary>The junction for an Anchor whose point on the circle is <paramref name="point"/>.</summary>
    public int Near(NodePoint point)
    {
        if (_green.Length > 0)
        {
            var green = Nearest(_green, point);
            if (_slice.Node(green).DistanceTo(point) <= GreenReachM) return green;
        }
        return Nearest(_candidates, point);
    }

    private int Nearest(int[] junctions, NodePoint point) => junctions.MinBy(n => _slice.Node(n).DistanceTo(point));

    // On green ground: at least half the usable ways meeting there are Off-road or Traffic-free.
    private bool OnGreen(int junction)
    {
        int green = 0, usable = 0;
        foreach (var s in _slice.SegmentsAt(junction))
        {
            var segment = _slice.Segments[s];
            if (segment.Access == AccessKind.Forbidden) continue;
            usable++;
            if (OffRoadNetworks.IsGreen(segment)) green++;
        }
        return usable > 0 && green * 2 >= usable;
    }
}
