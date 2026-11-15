using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Loops;

/// <summary>
/// Destinations (PLAN T041, the user's choice of one or two per Loop): Loops from the start
/// through one or two gates of large Off-road networks within reach and back, each Path the
/// router's, ground already used dearer. Between neighbouring gates of a park the cheapest way is
/// usually round its edge, and back from a gate up a river the way home takes the other bank: the
/// shapes of Trail Router's best Loops, with no Anchor to pull the route away.
/// </summary>
public static class DestinationLoops
{
    /// <summary>An Off-road network with this much green ground is a destination.</summary>
    public const double DestinationM = 3000;

    /// <summary>Whether a Segment is green ground in a destination (T047).</summary>
    public static bool InDestination(NetworkSlice slice, Segment segment)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(segment);
        return OffRoadNetworks.IsGreen(segment) && IsDestination(slice.OffRoadNetworks, segment.From);
    }

    /// <summary>Gates per destination, spread out by farthest-point sampling.</summary>
    private const int GatesPerDestination = 10;

    /// <summary>Loops built with the router, those estimated nearest the Distance first.</summary>
    public const int MostBuilt = 60;

    /// <summary>
    /// The candidates, in a fixed order. They're built in parallel, a search per thread, since a
    /// Loop's Paths don't depend on another Loop's (review after T030: "run candidates in
    /// parallel").
    /// </summary>
    public static IReadOnlyList<Loop> Candidates(NetworkSlice slice, int start, double distanceM, ScoringWeights weights,
        CancellationToken cancellationToken = default) =>
        Build(slice, start, Plan(slice, start, distanceM, weights), weights, cancellationToken: cancellationToken);

    /// <summary>
    /// The gates each candidate goes through, nearest the Distance first, at most
    /// <see cref="MostBuilt"/>: known before any is built, so a search can say how many it will try (T054).
    /// </summary>
    public static IReadOnlyList<int[]> Plan(NetworkSlice slice, int start, double distanceM, ScoringWeights weights)
    {
        ArgumentNullException.ThrowIfNull(slice);
        var networks = slice.OffRoadNetworks;
        var reach = LengthsFrom(slice, start, new RunCostModel(weights));
        bool IsDestination(int junction) => DestinationLoops.IsDestination(networks, junction);
        IEnumerable<int> NextTo(int junction) => Neighbours(slice, junction);

        // Gates: junctions of a destination with a usable Segment leading out of it, near enough
        // to go there and back.
        var gatesOf = new Dictionary<int, List<int>>();
        for (var j = 0; j < slice.NodeCount; j++)
        {
            var n = networks.NetworkAt(j);
            if (n < 0 || networks.MetresOf(n) < DestinationM || 2 * reach[j] > 1.2 * distanceM) continue;
            foreach (var s in slice.SegmentsAt(j))
            {
                var segment = slice.Segments[s];
                if (segment.Access == AccessKind.Forbidden || networks.NetworkAt(segment.From == j ? segment.To : segment.From) == n) continue;
                (gatesOf.TryGetValue(n, out var gates) ? gates : gatesOf[n] = []).Add(j);
                break;
            }
        }

        // Both ends of each bridge over water are gates too, of the destination they meet or one a
        // Segment away (T046): a Loop along a river crosses back at a bridge, not through streets.
        var bridgeEnds = new List<int>();
        foreach (var segment in slice.Segments)
        {
            if (!segment.BridgeOverWater || segment.Access == AccessKind.Forbidden) continue;
            foreach (var end in new[] { segment.From, segment.To })
            {
                if (2 * reach[end] > 1.2 * distanceM) continue;
                if (IsDestination(end) || NextTo(end).Any(IsDestination)) bridgeEnds.Add(end);
            }
        }

        // Spread out: the gate nearest the start, then each next the farthest from those chosen,
        // which works along a river as well as round a park.
        var spread = new List<int>();
        foreach (var gates in gatesOf.Values)
        {
            var chosen = new List<int> { gates.MinBy(g => reach[g]) };
            while (chosen.Count < Math.Min(GatesPerDestination, gates.Count))
                chosen.Add(gates.Where(g => !chosen.Contains(g)).MaxBy(g => chosen.Min(c => slice.Node(c).DistanceTo(slice.Node(g)))));
            spread.AddRange(chosen);
        }
        spread.AddRange(bridgeEnds.Distinct().Except(spread));

        // Out to one gate and back another way, or through two, estimated by the lengths to them.
        var shapes = new List<(int[] Gates, double Estimate)>();
        foreach (var g in spread) shapes.Add(([g], 2.2 * reach[g]));
        foreach (var a in spread)
        {
            foreach (var b in spread)
            {
                if (a != b) shapes.Add(([a, b], reach[a] + (1.25 * slice.Node(a).DistanceTo(slice.Node(b))) + reach[b]));
            }
        }

        return [.. shapes.OrderBy(s => Math.Abs(s.Estimate - distanceM)).Take(MostBuilt).Select(s => s.Gates)];
    }

    /// <summary>
    /// The candidates through these gates, in their order, each told to <paramref name="built"/> as
    /// it's built (or none if it couldn't be); their Paths guided by <paramref name="landmarks"/>,
    /// if given, placed under <see cref="RunCostModel.Cheapest"/> for these weights (T068).
    /// </summary>
    public static IReadOnlyList<Loop> Build(NetworkSlice slice, int start, IReadOnlyList<int[]> nearest, ScoringWeights weights,
        Action<Loop?>? built = null, Landmarks? landmarks = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(nearest);
        var loops = new Loop?[nearest.Count];
        Parallel.For(0, nearest.Count, new ParallelOptions { CancellationToken = cancellationToken },
            () => new PathSearch(slice),
            (i, _, search) =>
            {
                loops[i] = Through(slice, search, start, nearest[i], weights, landmarks);
                built?.Invoke(loops[i]);
                return search;
            },
            _ => { });
        return [.. loops.OfType<Loop>()];
    }

    // Whether a junction is in an Off-road network large enough to be a destination; and the
    // junctions a usable Segment away from one.
    private static bool IsDestination(OffRoadNetworks networks, int junction) =>
        networks.NetworkAt(junction) is var n && n >= 0 && networks.MetresOf(n) >= DestinationM;

    private static IEnumerable<int> Neighbours(NetworkSlice slice, int junction)
    {
        foreach (var s in slice.SegmentsAt(junction).ToArray())
        {
            var segment = slice.Segments[s];
            if (segment.Access != AccessKind.Forbidden) yield return segment.From == junction ? segment.To : segment.From;
        }
    }

    private static Loop? Through(NetworkSlice slice, PathSearch search, int start, int[] gates, ScoringWeights weights, Landmarks? landmarks)
    {
        int[] stops = [start, .. gates, start];
        var used = new HashSet<int>();
        var traversals = new List<Traversal>();
        for (var i = 0; i + 1 < stops.Length; i++)
        {
            var path = search.Find(PathEnd.AtNode(stops[i]), PathEnd.AtNode(stops[i + 1]), new RunCostModel(weights, used), landmarks);
            if (path is null) return null;
            traversals.AddRange(path.Traversals);
            foreach (var t in path.Traversals) slice.Segments[t.Segment].Cover(used);
        }
        return new Loop(start, gates, traversals);
    }

    // The length of the cheapest way from the start to every junction, by the router's cost.
    private static double[] LengthsFrom(NetworkSlice slice, int start, RunCostModel cost)
    {
        var best = new double[slice.NodeCount];
        var length = new double[slice.NodeCount];
        Array.Fill(best, double.PositiveInfinity);
        Array.Fill(length, double.PositiveInfinity);
        best[start] = length[start] = 0;
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var node, out var reached))
        {
            if (reached > best[node]) continue;
            foreach (var s in slice.SegmentsAt(node))
            {
                var segment = slice.Segments[s];
                if (segment.Access == AccessKind.Forbidden) continue;
                var forward = segment.From == node;
                var next = forward ? segment.To : segment.From;
                var further = reached + cost.CostOf(segment, forward);
                if (further >= best[next]) continue;
                best[next] = further;
                length[next] = length[node] + segment.LengthM;
                queue.Enqueue(next, further);
            }
        }
        return length;
    }
}
