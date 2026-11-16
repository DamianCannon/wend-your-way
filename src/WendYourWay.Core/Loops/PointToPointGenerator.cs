using System.Collections.Concurrent;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Loops;

/// <summary>
/// What the user asks for to go from one place to another (STORY-037, T090): a Point-to-point
/// from <paramref name="Start"/> to <paramref name="End"/> (British National Grid), a Run or a
/// Gravel Ride, at the Leans, of about <paramref name="DistanceKm"/>; or, with none, a spread
/// from near the shortest way to the greenest.
/// </summary>
public sealed record PointToPointRequest(
    NodePoint Start, NodePoint End, double? DistanceKm = null, double HillsLean = 0, double OffRoadLean = 0, Activity Activity = Activity.Run)
{
    public double? DistanceKm { get; } = DistanceKm is null or (>= Request.MinDistanceKm and <= Request.MaxDistanceKm)
        ? DistanceKm
        : throw new ArgumentOutOfRangeException(nameof(DistanceKm), DistanceKm, $"A Distance must be from {Request.MinDistanceKm} to {Request.MaxDistanceKm} km.");

    /// <summary>Its Leans together.</summary>
    public Leans Leans => new(HillsLean, OffRoadLean);

    /// <summary>The shortest route offered with a Distance: a Loop's window (R1.3).</summary>
    public double? ShortestKm => DistanceKm - Request.WindowKm;

    /// <summary>The longest route offered with a Distance: a Loop's window (R1.3).</summary>
    public double? LongestKm => DistanceKm + Math.Max(Request.WindowKm, Request.LongerShare * DistanceKm ?? 0);
}

/// <summary>
/// The Point-to-points offered, in order: with no Distance, shortest first; with one, best first.
/// <paramref name="ShortestM"/> is the shortest way from the start to the end on the ways the
/// Activity may use; null if there's none. None are offered under a Distance shorter than it.
/// </summary>
public sealed record PointToPoints(double? ShortestM, IReadOnlyList<Loop> Offered);

/// <summary>
/// Generates Point-to-points (T090) as <see cref="LoopGenerator"/> does Loops, but from the start
/// to the end: Anchors placed about an ellipse whose foci are the two, each Path found by the
/// router's cost model with ground already used made dearer, scored as a Loop is.
/// </summary>
public static class PointToPointGenerator
{
    /// <summary>With no Distance, the first offered is the best within this share over the shortest way.</summary>
    public const double FirstWithinShare = 0.1;

    /// <summary>With no Distance, each offered after the first is at least this share longer than the one before.</summary>
    public const double StepShare = 0.1;

    /// <summary>With no Distance, none offered is longer than this many times the shortest way.</summary>
    public const double LongestTimesShortest = 1.5;

    // Of those a step longer and scoring better, the best up to this share longer than the one
    // before, so the spread climbs in steps instead of leaping to the greenest at once.
    private const double StepReachShare = 0.25;

    // With no Distance, how much the ways wind is learnt aiming at this multiple of the shortest
    // way, then routes are aimed at each of these: one pass each, half the routes of three
    // targets with corrections either side, for the same span (T090, 2026-11-08).
    private const double SpreadFirstTarget = 1.25;
    private static readonly double[] SpreadTargets = [1.05, 1.15, 1.25, 1.35, 1.45];

    private const double FirstDetourGuess = 1.3;
    private static readonly double[] CorrectedScales = [0.9, 1.0, 1.1];

    // Where the Anchors sit on the ellipse, by its angle (degrees: 0 at the end's side, 180 at the
    // start's), one or two of them, each on either side of the straight line.
    private static readonly double[][] Shapes = [[90], [60], [120], [45], [135], [120, 60], [150, 90], [90, 30]];

    /// <summary>The Point-to-points offered for the Request; none if none can be made.</summary>
    public static PointToPoints Find(NetworkSlice slice, PointToPointRequest request, ScoringWeights weights, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(weights);
        weights = weights.For(request.Activity);
        var leaning = request.Leans.On(weights);
        var none = new PointToPoints(null, []);

        var junctions = LoopGenerator.LargestUsablePiece(slice);
        if (junctions.Length == 0) return none;
        var start = Nearest(slice, junctions, request.Start);
        var end = Nearest(slice, junctions, request.End);
        if (start == end || slice.Node(start).DistanceTo(request.Start) > Request.SnapMarginM || slice.Node(end).DistanceTo(request.End) > Request.SnapMarginM) return none;

        var search = new PathSearch(slice);
        if (search.Find(PathEnd.AtNode(start), PathEnd.AtNode(end), new Shortest()) is not { } shortest) return none;
        var shortestM = shortest.LengthM;
        if (request.DistanceKm * 1000 < shortestM) return new PointToPoints(shortestM, []);

        int[] throughRoutes = [.. junctions.Where(n => LoopGenerator.UsableSegmentsAt(slice, n) >= 3)];
        var anchors = new AnchorPlacement(slice, throughRoutes);
        var longest = request.LongestKm * 1000 ?? LongestTimesShortest * shortestM;
        var landmarks = longest >= Request.LongFromKm * 1000 ? Landmarks.Place(slice, junctions, LoopGenerator.LandmarksPlaced, RunCostModel.Cheapest(leaning), cancellationToken) : null;

        var candidates = new List<Loop> { new(start, [], shortest.Traversals) };
        if (Route(slice, search, start, end, [], leaning, landmarks) is { } direct) candidates.Add(direct);

        // A first guess at how much the ways wind, then routes aimed by what it measured.
        var (firstM, aimedM) = request.DistanceKm is { } km
            ? (km * 1000, CorrectedScales.Select(s => s * km * 1000).ToArray())
            : (SpreadFirstTarget * shortestM, SpreadTargets.Select(t => t * shortestM).ToArray());
        var searches = new ConcurrentBag<PathSearch>();
        var (from, to) = (slice.Node(start), slice.Node(end));
        var first = Build(slice, searches, anchors, start, end, from, to, [firstM / FirstDetourGuess], leaning, landmarks, cancellationToken);
        candidates.AddRange(first.Select(f => f.Loop));
        if (first.Count > 0)
        {
            var detour = Median(first.Select(f => LengthOf(slice, f.Loop) / f.CrowM));
            candidates.AddRange(Build(slice, searches, anchors, start, end, from, to, [.. aimedM.Select(m => m / detour)], leaning, landmarks, cancellationToken)
                .Select(f => f.Loop));
        }

        var scored = candidates
            .Select(l => (Loop: l, LengthM: LengthOf(slice, l), Score: LoopScorer.Score(slice, l, leaning)))
            .ToArray();
        return new PointToPoints(shortestM, request.DistanceKm is null
            ? [.. Spread(scored, shortestM)]
            : Best(slice, [.. scored.Where(c => c.LengthM >= request.ShortestKm * 1000 && c.LengthM <= longest)]));
    }

    /// <summary>
    /// The spread offered with no Distance, from <paramref name="scored"/> routes: the best within
    /// <see cref="FirstWithinShare"/> of the shortest way, then each next at least
    /// <see cref="StepShare"/> longer than the one before and scoring better (the best of those up
    /// to a quarter longer, or else the nearest), none past <see cref="LongestTimesShortest"/>.
    /// </summary>
    public static IReadOnlyList<Loop> Spread(IReadOnlyList<(Loop Loop, double LengthM, double Score)> scored, double shortestM)
    {
        ArgumentNullException.ThrowIfNull(scored);
        var pool = scored.Where(c => c.LengthM <= LongestTimesShortest * shortestM).ToArray();
        var offered = new List<(Loop Loop, double LengthM, double Score)>();
        var near = pool.Where(c => c.LengthM <= (1 + FirstWithinShare) * shortestM).ToArray();
        if (near.Length == 0) return [];
        offered.Add(near.MaxBy(c => c.Score));
        while (offered.Count < LoopGenerator.Offered)
        {
            var last = offered[^1];
            var better = pool.Where(c => c.LengthM >= (1 + StepShare) * last.LengthM && c.Score > last.Score).ToArray();
            if (better.Length == 0) break;
            var reach = Math.Max((1 + StepReachShare) * last.LengthM, better.Min(c => c.LengthM));
            offered.Add(better.Where(c => c.LengthM <= reach).MaxBy(c => c.Score));
        }
        return [.. offered.Select(o => o.Loop)];
    }

    // With a Distance: the best first, then each next best distinct from those offered and good
    // enough, as a Request's Loops are, but for heading: every one heads for the end.
    private static List<Loop> Best(NetworkSlice slice, (Loop Loop, double LengthM, double Score)[] inWindow)
    {
        var offered = new List<(Loop Loop, IReadOnlySet<int> Covered, double Score)>();
        foreach (var (loop, _, score) in inWindow.OrderByDescending(c => c.Score))
        {
            if (offered.Count == LoopGenerator.Offered || (offered.Count > 0 && score < LoopGenerator.LeastScoreShare * offered[0].Score)) break;
            var covered = SharedGround.Covered(slice, loop);
            if (!offered.All(o => SharedGround.Share(slice, o.Loop, o.Covered, loop, covered) <= LoopGenerator.MostShared)) continue;
            offered.Add((loop, covered, score));
        }
        return [.. offered.Select(o => o.Loop)];
    }

    // The routes through each shape's Anchors on ellipses whose Anchors make each crow's length,
    // built in parallel, a search per thread, in a fixed order whichever finishes first.
    private static List<(Loop Loop, double CrowM)> Build(
        NetworkSlice slice, ConcurrentBag<PathSearch> searches, AnchorPlacement placement, int start, int end, NodePoint from, NodePoint to,
        double[] crowLengthsM, ScoringWeights weights, Landmarks? landmarks, CancellationToken cancellationToken)
    {
        var tries = crowLengthsM.SelectMany(crow => Shapes.SelectMany(shape => new[] { 1, -1 }.Select(side => (Crow: crow, Shape: shape, Side: side)))).ToArray();
        var built = new (Loop? Loop, double CrowM)[tries.Length];
        Parallel.For(0, tries.Length, new ParallelOptions { CancellationToken = cancellationToken },
            () => searches.TryTake(out var search) ? search : new PathSearch(slice),
            (i, _, search) =>
            {
                var (crow, shape, side) = tries[i];
                if (OnEllipse(from, to, shape, side, crow) is not { } points) return search;
                int[] stops = [.. points.Select(placement.Near)];
                if (stops.Contains(start) || stops.Contains(end) || stops.Distinct().Count() != stops.Length) return search;
                built[i] = (Route(slice, search, start, end, stops, weights, landmarks), crow);
                return search;
            },
            searches.Add);
        return [.. built.Where(b => b.Loop is not null).Select(b => (b.Loop!, b.CrowM))];
    }

    // Start → Anchors → end, each Path dearer on ground the ones before used.
    private static Loop? Route(NetworkSlice slice, PathSearch search, int start, int end, int[] anchors, ScoringWeights weights, Landmarks? landmarks)
    {
        int[] stops = [start, .. anchors, end];
        var used = new HashSet<int>();
        var traversals = new List<Traversal>();
        for (var i = 0; i + 1 < stops.Length; i++)
        {
            var path = search.Find(PathEnd.AtNode(stops[i]), PathEnd.AtNode(stops[i + 1]), new RunCostModel(weights, used), landmarks);
            if (path is null) return null;
            traversals.AddRange(path.Traversals);
            foreach (var t in path.Traversals) slice.Segments[t.Segment].Cover(used);
        }
        return new Loop(start, anchors, traversals);
    }

    /// <summary>
    /// The Anchors' points for a shape on the ellipse, with these foci, on which start → Anchors →
    /// end is <paramref name="crowM"/> as the crow flies; null if that's no longer than straight.
    /// </summary>
    internal static NodePoint[]? OnEllipse(NodePoint from, NodePoint to, double[] shape, int side, double crowM)
    {
        var c = from.DistanceTo(to) / 2;
        if (c == 0 || crowM <= 2 * c * 1.02) return null;
        var (ux, uy) = ((to.X - from.X) / (2 * c), (to.Y - from.Y) / (2 * c));
        var (vx, vy) = (-uy * side, ux * side);
        var (mx, my) = ((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        NodePoint[] At(double a)
        {
            var b = Math.Sqrt(Math.Max(0, (a * a) - (c * c)));
            return [.. shape.Select(degrees =>
            {
                var t = degrees * Math.PI / 180;
                var (x, y) = (a * Math.Cos(t), b * Math.Sin(t));
                return new NodePoint(mx + (x * ux) + (y * vx), my + (x * uy) + (y * vy));
            })];
        }
        double Crow(double a)
        {
            NodePoint[] stops = [from, .. At(a), to];
            return Enumerable.Range(0, stops.Length - 1).Sum(i => stops[i].DistanceTo(stops[i + 1]));
        }
        // The crow's length grows with the ellipse: halve the gap till it's close.
        double low = c, high = c + crowM;
        for (var i = 0; i < 40; i++)
        {
            var mid = (low + high) / 2;
            if (Crow(mid) < crowM) low = mid;
            else high = mid;
        }
        return At((low + high) / 2);
    }

    private static double LengthOf(NetworkSlice slice, Loop loop) => loop.Traversals.Sum(t => slice.Segments[t.Segment].LengthM);

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static int Nearest(NetworkSlice slice, int[] junctions, NodePoint point) => LoopGenerator.Nearest(slice, junctions, point);

    // The shortest way on ways the Activity may use: length alone.
    private sealed class Shortest : ICostModel
    {
        public double FloorPerMetre => 1;

        public double CostOf(Segment segment, bool forward) => segment.Access == AccessKind.Forbidden ? double.PositiveInfinity : segment.LengthM;
    }
}
