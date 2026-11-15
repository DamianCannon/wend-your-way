using WendYourWay.Core.Network;

namespace WendYourWay.Core.Routing;

/// <summary>
/// A* search for the cheapest Path between two points of a <see cref="NetworkSlice"/>.
/// Deterministic: when Paths tie exactly, the choice depends only on the data (junction numbers
/// and OSM identities), never on the order Segments were loaded in.
/// </summary>
public static class PathFinder
{
    /// <summary>
    /// The cheapest Path from <paramref name="from"/> to <paramref name="to"/>, or null if none
    /// exists. For many Paths over one slice, a <see cref="PathSearch"/> reuses its memory.
    /// </summary>
    public static SegmentPath? Find(NetworkSlice slice, PathEnd from, PathEnd to, ICostModel cost) =>
        new PathSearch(slice).Find(from, to, cost);
}

/// <summary>
/// <see cref="PathFinder"/>'s A* with its working memory kept between Paths over one slice: a
/// Loop takes a few hundred, and arrays the size of the slice for each came to gigabytes (T018).
/// Each search stamps the junctions it touches, so the next starts clean without clearing
/// anything. Not thread-safe: one per thread.
/// </summary>
public sealed class PathSearch
{
    // Equal estimates are settled lowest junction number first.
    private sealed class ByEstimateThenNode : IComparer<(double Estimate, int Node)>
    {
        public static readonly ByEstimateThenNode Instance = new();

        public int Compare((double Estimate, int Node) x, (double Estimate, int Node) y)
        {
            var byEstimate = x.Estimate.CompareTo(y.Estimate);
            return byEstimate != 0 ? byEstimate : x.Node.CompareTo(y.Node);
        }
    }

    private readonly NetworkSlice _slice;
    private readonly double[] _best;
    private readonly int[] _arrivedBy;
    private readonly bool[] _arrivedForward;
    private readonly int[] _reachedIn;   // the search that last reached each junction
    private readonly int[] _settledIn;   // the search that last settled it
    private readonly PriorityQueue<int, (double, int)> _open = new(ByEstimateThenNode.Instance);
    private int _search;

    public PathSearch(NetworkSlice slice)
    {
        ArgumentNullException.ThrowIfNull(slice);
        _slice = slice;
        var n = slice.NodeCount;
        _best = new double[n];
        _arrivedBy = new int[n];
        _arrivedForward = new bool[n];
        _reachedIn = new int[n];
        _settledIn = new int[n];
    }

    /// <summary>
    /// The cheapest Path from <paramref name="from"/> to <paramref name="to"/>, or null if none
    /// exists; guided by <paramref name="landmarks"/> too, if given (T068), placed under a cost
    /// model charging no Segment more than <paramref name="cost"/> does.
    /// </summary>
    public SegmentPath? Find(PathEnd from, PathEnd to, ICostModel cost, Landmarks? landmarks = null)
    {
        ArgumentNullException.ThrowIfNull(cost);
        var floor = cost.FloorPerMetre;
        if (!(floor > 0) || double.IsInfinity(floor))
            throw new ArgumentException($"A cost model's floor must be a positive number of cost units per metre, not {floor}.", nameof(cost));

        Begin();
        var slice = _slice;
        var target = slice.Node(to.Node);
        Reach(from.Node, 0, -1, false);
        // The larger of two lower bounds is a lower bound, and as consistent as both.
        double Estimate(int node) => Math.Max(slice.Node(node).DistanceTo(target) * floor, landmarks?.Bound(node, to.Node) ?? 0);
        _open.Enqueue(from.Node, (Estimate(from.Node), from.Node));

        while (_open.TryDequeue(out var node, out _))
        {
            if (node == to.Node) return Trace(from.Node, to.Node);
            if (_settledIn[node] == _search) continue;
            _settledIn[node] = _search;

            foreach (var s in slice.SegmentsAt(node))
            {
                var segment = slice.Segments[s];
                if (segment.From == segment.To) continue;
                var forward = segment.From == node;
                var next = forward ? segment.To : segment.From;
                var step = cost.CostOf(segment, forward);
                if (double.IsPositiveInfinity(step)) continue;
                if (!(step >= floor * segment.LengthM * (1 - 1e-12)))
                    throw new InvalidOperationException(
                        $"The cost model charged {step} for Segment {segment.Key} ({segment.LengthM} m), below its own floor " +
                        $"of {floor} per metre. A* would return wrong Paths; lower FloorPerMetre to cover every discount.");

                var reached = _best[node] + step;
                var bestNext = Best(next);
                if (reached < bestNext)
                {
                    Reach(next, reached, s, forward);
                    _open.Enqueue(next, (reached + Estimate(next), next));
                }
                else if (reached == bestNext && next != from.Node && IsBefore(segment.Key, slice.Segments[_arrivedBy[next]].Key))
                {
                    // An exactly equal way in: keep the one with the smaller OSM identity.
                    _arrivedBy[next] = s;
                    _arrivedForward[next] = forward;
                }
            }
        }

        return null;
    }

    // A new search: every junction's stamp is now out of date. After two billion searches the
    // stamps would wrap, so they're cleared first.
    private void Begin()
    {
        _open.Clear();
        if (_search == int.MaxValue)
        {
            Array.Clear(_reachedIn);
            Array.Clear(_settledIn);
            _search = 0;
        }
        _search++;
    }

    private double Best(int node) => _reachedIn[node] == _search ? _best[node] : double.PositiveInfinity;

    private void Reach(int node, double cost, int by, bool forward)
    {
        _reachedIn[node] = _search;
        _best[node] = cost;
        _arrivedBy[node] = by;
        _arrivedForward[node] = forward;
    }

    private static bool IsBefore(SegmentKey a, SegmentKey b) =>
        a.OsmWayId != b.OsmWayId ? a.OsmWayId < b.OsmWayId
        : a.FromNodeId != b.FromNodeId ? a.FromNodeId < b.FromNodeId
        : a.ToNodeId < b.ToNodeId;

    private SegmentPath Trace(int from, int to)
    {
        var traversals = new List<Traversal>();
        var length = 0.0;
        for (var node = to; node != from;)
        {
            var segment = _slice.Segments[_arrivedBy[node]];
            traversals.Add(new Traversal(_arrivedBy[node], _arrivedForward[node]));
            length += segment.LengthM;
            node = _arrivedForward[node] ? segment.From : segment.To;
        }
        traversals.Reverse();
        return new SegmentPath(traversals, _best[to], length);
    }
}
