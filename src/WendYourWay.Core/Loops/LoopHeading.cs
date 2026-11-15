using WendYourWay.Core.Network;

namespace WendYourWay.Core.Loops;

/// <summary>Which way a Loop goes from its start (PLAN T052).</summary>
public static class LoopHeading
{
    /// <summary>A Loop whose middle is nearer its start than this share of its length goes round it.</summary>
    public const double RoundShare = 0.1;

    /// <summary>
    /// The bearing in degrees (0 north, clockwise) from the Loop's start to the middle of its
    /// ground, its Segments' midpoints weighted by length; null for a Loop that goes round its start.
    /// </summary>
    public static double? Of(NetworkSlice slice, Loop loop)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);
        double x = 0, y = 0, length = 0;
        foreach (var traversal in loop.Traversals)
        {
            var segment = slice.Segments[traversal.Segment];
            var (from, to) = (slice.Node(segment.From), slice.Node(segment.To));
            x += (from.X + to.X) / 2 * segment.LengthM;
            y += (from.Y + to.Y) / 2 * segment.LengthM;
            length += segment.LengthM;
        }
        if (length == 0) return null;
        var start = slice.Node(loop.Start);
        var (east, north) = ((x / length) - start.X, (y / length) - start.Y);
        if (Math.Sqrt((east * east) + (north * north)) < RoundShare * length) return null;
        return (Math.Atan2(east, north) * 180 / Math.PI + 360) % 360;
    }
}
