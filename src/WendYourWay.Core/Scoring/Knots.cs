using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;

namespace WendYourWay.Core.Scoring;

/// <summary>
/// Knots (CONTEXT.md): the junctions a Loop comes back to other than while retracing its own
/// steps, arriving or leaving on fresh ground; its last return to the start aside. A spur to touch
/// somewhere, a lollipop and a tangle each make them; out and back the same way doesn't (T042).
/// </summary>
public static class Knots
{
    public static int Of(NetworkSlice slice, Loop loop)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);

        // Whether each Traversal is on fresh ground: most of it not covered before.
        var covered = new HashSet<int>();
        var fresh = new bool[loop.Traversals.Count];
        for (var i = 0; i < loop.Traversals.Count; i++)
        {
            var segment = slice.Segments[loop.Traversals[i].Segment];
            fresh[i] = segment.RetracedMetres(covered) < segment.LengthM / 2;
            segment.Cover(covered);
        }

        var visited = new HashSet<int> { loop.Start };
        var knots = 0;
        for (var i = 0; i + 1 < loop.Traversals.Count; i++)
        {
            var traversal = loop.Traversals[i];
            var segment = slice.Segments[traversal.Segment];
            var at = traversal.Forward ? segment.To : segment.From;
            if (!visited.Add(at) && (fresh[i] || fresh[i + 1])) knots++;
        }
        return knots;
    }
}
