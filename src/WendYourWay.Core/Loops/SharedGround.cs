using WendYourWay.Core.Network;

namespace WendYourWay.Core.Loops;

/// <summary>How much ground two Loops share, by same-ground group (R1.4).</summary>
public static class SharedGround
{
    /// <summary>
    /// The larger of each Loop's metres on ground the other covers, over its own length: 0 for
    /// Loops with nothing in common, 1 when one lies wholly on the other's ground.
    /// </summary>
    public static double Share(NetworkSlice slice, Loop a, Loop b) =>
        Share(slice, a, Covered(slice, a), b, Covered(slice, b));

    /// <summary>The same, with each Loop's ground already found by <see cref="Covered"/>.</summary>
    public static double Share(NetworkSlice slice, Loop a, IReadOnlySet<int> coveredByA, Loop b, IReadOnlySet<int> coveredByB) =>
        Math.Max(On(slice, a, coveredByB), On(slice, b, coveredByA));

    /// <summary>The same-ground groups a Loop covers.</summary>
    public static IReadOnlySet<int> Covered(NetworkSlice slice, Loop loop)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);
        var covered = new HashSet<int>();
        foreach (var traversal in loop.Traversals) slice.Segments[traversal.Segment].Cover(covered);
        return covered;
    }

    // The share of the Loop's length on ground already covered.
    private static double On(NetworkSlice slice, Loop loop, IReadOnlySet<int> covered)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);
        double on = 0, length = 0;
        foreach (var traversal in loop.Traversals)
        {
            var segment = slice.Segments[traversal.Segment];
            on += segment.RetracedMetres(covered);
            length += segment.LengthM;
        }
        return length > 0 ? on / length : 0;
    }
}
