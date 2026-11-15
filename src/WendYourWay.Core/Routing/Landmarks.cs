using WendYourWay.Core.Network;

namespace WendYourWay.Core.Routing;

/// <summary>
/// A few junctions round the edge of a slice, and the cheapest cost from each out to every
/// junction and from every junction back (PLAN T068). By the triangle inequality they bound the
/// cost still to go from any junction to a Path's end far better than the straight line at the
/// cheapest rate, so <see cref="PathSearch"/> looks at far fewer junctions: in a 100 km slice a
/// sixth as many. Worked out under a cost model no dearer than any the search is then given, the
/// bound never overestimates, and the Paths found are as cheap as without. Read-only once placed:
/// shared by every thread's search.
/// </summary>
public sealed class Landmarks
{
    private readonly double[][] _outOf;   // _outOf[k][n]: cheapest cost from landmark k to junction n
    private readonly double[][] _into;    // _into[k][n]: from junction n to landmark k

    private Landmarks(double[][] outOf, double[][] into) => (_outOf, _into) = (outOf, into);

    /// <summary>
    /// <paramref name="count"/> landmarks among <paramref name="junctions"/>: in each of as many
    /// equal sectors round their middle, the junction farthest from it. Costs come from
    /// <paramref name="cheapest"/>, which must charge no Segment more than any model the
    /// landmarks will guide. Stops when <paramref name="cancellationToken"/> is cancelled: the map
    /// has moved on.
    /// </summary>
    public static Landmarks Place(NetworkSlice slice, IReadOnlyList<int> junctions, int count, ICostModel cheapest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(junctions);
        ArgumentNullException.ThrowIfNull(cheapest);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        int[] marks = [.. Edge(slice, junctions, count)];
        var outOf = new double[marks.Length][];
        var into = new double[marks.Length][];
        Parallel.For(0, marks.Length * 2, new ParallelOptions { CancellationToken = cancellationToken }, j =>
        {
            var k = j / 2;
            if (j % 2 == 0) outOf[k] = Cheapest(slice, marks[k], cheapest, outward: true, cancellationToken);
            else into[k] = Cheapest(slice, marks[k], cheapest, outward: false, cancellationToken);
        });
        return new Landmarks(outOf, into);
    }

    /// <summary>A lower bound on the cost of the cheapest Path from <paramref name="node"/> to <paramref name="target"/>.</summary>
    public double Bound(int node, int target)
    {
        var bound = 0.0;
        for (var k = 0; k < _outOf.Length; k++)
        {
            // Landmark to target costs no more than landmark to node to target; node to landmark no
            // more than node to target to landmark. Unreachable either way says nothing.
            var past = _outOf[k][target] - _outOf[k][node];
            var back = _into[k][node] - _into[k][target];
            if (past > bound && double.IsFinite(past)) bound = past;
            if (back > bound && double.IsFinite(back)) bound = back;
        }
        return bound;
    }

    // In each sector round the junctions' middle, the one farthest from it, lowest number on a tie.
    private static IEnumerable<int> Edge(NetworkSlice slice, IReadOnlyList<int> junctions, int count)
    {
        if (junctions.Count == 0) return [];
        var (x, y) = (junctions.Average(n => slice.Node(n).X), junctions.Average(n => slice.Node(n).Y));
        var farthest = new int[count];
        var farthestM = new double[count];
        Array.Fill(farthest, -1);
        foreach (var n in junctions)
        {
            var (dx, dy) = (slice.Node(n).X - x, slice.Node(n).Y - y);
            var sector = Math.Min(count - 1, (int)((Math.Atan2(dx, dy) + Math.PI) / (2 * Math.PI) * count));
            var metres = Math.Sqrt((dx * dx) + (dy * dy));
            if (farthest[sector] < 0 || metres > farthestM[sector] || (metres == farthestM[sector] && n < farthest[sector]))
                (farthest[sector], farthestM[sector]) = (n, metres);
        }
        return farthest.Where(n => n >= 0);
    }

    // Dijkstra from a landmark: outward, the cost from it to each junction; inward, from each to it.
    private static double[] Cheapest(NetworkSlice slice, int landmark, ICostModel cost, bool outward, CancellationToken cancellationToken)
    {
        var best = new double[slice.NodeCount];
        Array.Fill(best, double.PositiveInfinity);
        var settled = new bool[slice.NodeCount];
        var open = new PriorityQueue<int, double>();
        best[landmark] = 0;
        open.Enqueue(landmark, 0);
        while (open.TryDequeue(out var node, out _))
        {
            if (settled[node]) continue;
            settled[node] = true;
            // Each search is half a second across a 100 km slice: look up now and then.
            if ((node & 0xfff) == 0) cancellationToken.ThrowIfCancellationRequested();
            foreach (var s in slice.SegmentsAt(node))
            {
                var segment = slice.Segments[s];
                if (segment.From == segment.To) continue;
                var forward = segment.From == node;
                var next = forward ? segment.To : segment.From;
                // Inward, the way is travelled from next to node.
                var step = cost.CostOf(segment, outward ? forward : !forward);
                if (double.IsPositiveInfinity(step)) continue;
                if (best[node] + step < best[next])
                {
                    best[next] = best[node] + step;
                    open.Enqueue(next, best[next]);
                }
            }
        }
        return best;
    }
}
