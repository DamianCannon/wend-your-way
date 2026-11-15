namespace WendYourWay.Core.Routing;

/// <summary>
/// One Segment, traversed in one direction. <see cref="Forward"/> is true when the Segment is
/// travelled from its <c>From</c> node to its <c>To</c> node; ascent, GPX point order and Retrace
/// all depend on it.
/// </summary>
public readonly record struct Traversal(int Segment, bool Forward);

/// <summary>The cheapest way between two points of a slice: its Traversals in order, total cost and length.</summary>
public sealed record SegmentPath(IReadOnlyList<Traversal> Traversals, double Cost, double LengthM);

/// <summary>
/// Where a Path starts or ends. Only junctions for now; a point part-way along a Segment (a
/// snapped start, a circle Anchor) will be added here without changing <see cref="PathFinder"/>'s signature.
/// </summary>
public readonly record struct PathEnd(int Node)
{
    public static PathEnd AtNode(int node) => new(node);
}
