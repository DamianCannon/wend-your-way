using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;

namespace WendYourWay.Core.Loops;

/// <summary>
/// Spurs (PLAN T042): a Loop sent through a gate or an Anchor off its natural line goes out to it
/// and back, by the same way or a parallel one, and comes back to the junction it left: "a weird
/// loop out from Roehampton Gate that makes no sense" (user, 2026-10-23). Such an excursion, no
/// more than <see cref="LongestShare"/> of the Loop, is cut out; a longer one is a lobe of a
/// figure of eight, kept.
/// </summary>
public static class Spurs
{
    /// <summary>The longest excursion cut out, as a share of the Loop's length.</summary>
    public const double LongestShare = 0.4;

    /// <summary>The Loop with its excursions cut out, or the Loop itself if it has none.</summary>
    public static Loop Trim(NetworkSlice slice, Loop loop)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);
        var traversals = loop.Traversals.ToList();
        while (CutOne(slice, loop.Start, traversals)) { }
        if (traversals.Count == loop.Traversals.Count) return loop;

        var passed = Junctions(slice, loop.Start, traversals).ToHashSet();
        return new Loop(loop.Start, [.. loop.Anchors.Where(passed.Contains)], traversals);
    }

    // Cuts the first excursion short enough to be a spur, if there is one.
    private static bool CutOne(NetworkSlice slice, int start, List<Traversal> traversals)
    {
        var junctions = Junctions(slice, start, traversals);
        var total = traversals.Sum(t => slice.Segments[t.Segment].LengthM);
        for (var i = 0; i < traversals.Count; i++)
        {
            // The next return to this junction; from the start, not the Loop's own return to it.
            var j = junctions.FindIndex(i + 1, n => n == junctions[i]);
            if (j < 0 || (i == 0 && j == traversals.Count)) continue;
            var excursion = traversals.Skip(i).Take(j - i).Sum(t => slice.Segments[t.Segment].LengthM);
            if (excursion > LongestShare * total) continue;
            traversals.RemoveRange(i, j - i);
            return true;
        }
        return false;
    }

    // The junctions a Loop passes, its start first and last.
    private static List<int> Junctions(NetworkSlice slice, int start, List<Traversal> traversals)
    {
        var at = start;
        var junctions = new List<int>(traversals.Count + 1) { at };
        foreach (var t in traversals)
        {
            var segment = slice.Segments[t.Segment];
            at = t.Forward ? segment.To : segment.From;
            junctions.Add(at);
        }
        return junctions;
    }
}
