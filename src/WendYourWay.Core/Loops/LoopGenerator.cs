using System.Collections.Concurrent;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Loops;

/// <summary>
/// Generates Loops for a Request over a slice: from the start to one or two destinations
/// (<see cref="DestinationLoops"/>), or through two or three Anchors on a circle, and back; each
/// Path found by the router's cost model with ground already used made dearer. The best by the
/// score wins, whichever way it was made.
/// </summary>
public static class LoopGenerator
{
    // Real ways are longer than the shape through the Anchors as the crow flies, by a detour
    // factor: a first guess sizes the first circles, and what they measure sizes the rest.
    private const double FirstDetourGuess = 1.3;

    // Where the Anchors sit round a circle through the start (degrees from it), the perimeter of
    // that shape per unit of radius, and how many bearings its circles are tried on. Two Anchors
    // make a triangle (3√3·r); three, so a Loop can take in more than one large green area (user,
    // 2026-10-19), a square (4√2·r), on fewer bearings to keep within the time.
    private sealed record Shape(double[] AnchorTurns, double PerimeterPerRadius, int Bearings);

    private static readonly Shape[] Shapes =
    [
        new([120, 240], 3 * Math.Sqrt(3), 24),
        new([90, 180, 270], 4 * Math.Sqrt(2), 12),
    ];

    private static readonly double[] CorrectedScales = [0.9, 1.0, 1.1];

    /// <summary>How many Loops a Request offers at most (R1.4).</summary>
    public const int Offered = 5;

    /// <summary>The most ground two offered Loops may share, as a share of either's length (R1.4).</summary>
    public const double MostShared = 0.3;

    /// <summary>The least score a Loop after the first may have, as a share of the first's (T052).</summary>
    public const double LeastScoreShare = 0.5;

    /// <summary>How much more Main Road than the first a Loop after it may take (T052).</summary>
    public const double MainRoadAllowanceM = 200;

    /// <summary>
    /// The least score a Loop in another direction may have, as a share of the first's, added while
    /// fewer than five are on the usual bar (T096: "At least 0.3 of the first", user).
    /// </summary>
    public const double LeastScoreShareInAnotherDirection = 0.3;

    /// <summary>How much more Main Road than the first a Loop in another direction may take (T096: "Up to 2 km more", user).</summary>
    public const double MainRoadAllowanceInAnotherDirectionM = 2000;

    /// <summary>The least angle between the headings of two Loops offered (T052).</summary>
    public const double LeastAngleApart = 45;

    /// <summary>
    /// How many Landmarks guide a Request's Paths (T068): 8 settled a sixth of the junctions the
    /// straight line did across a 100 km slice; 16, hardly fewer.
    /// </summary>
    public const int LandmarksPlaced = 8;

    /// <summary>The best Loop for the Request, or null if none can be made: the first <see cref="Offer"/> offers.</summary>
    public static Loop? Generate(NetworkSlice slice, Request request, ScoringWeights weights, CancellationToken cancellationToken = default) =>
        Offer(slice, request, weights, cancellationToken) is [var best, ..] ? best : null;

    /// <summary>The Loops offered for the Request, best first (R1.4); none if none can be made.</summary>
    public static LoopsOffered Offer(NetworkSlice slice, Request request, ScoringWeights weights, CancellationToken cancellationToken = default) =>
        Find(slice, request, weights, cancellationToken: cancellationToken).Offer(request.Leans);

    /// <summary>
    /// Every Loop found for the Request in its length window, kept so the Loops offered can be
    /// chosen again under another Hills Lean without a new search (T053). How far it has got goes
    /// to <paramref name="progress"/> as each Loop is tried, one report at a time (T054).
    /// </summary>
    public static FoundLoops Find(
        NetworkSlice slice, Request request, ScoringWeights weights, Action<SearchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(weights);
        // The weights for the Request's Activity (T069): every cost model it makes, its Paths', its
        // scores' and its Landmarks', is that Activity's; a Run's ranked as the user likes (T105).
        weights = weights.Ranking(request.Activity);
        // This Request's own copy: every cost model it builds leans with it (T050).
        var leaning = request.Leans.On(weights);
        FoundLoops None() => new(slice, weights, []);

        var distanceM = request.DistanceKm * 1000;

        // Loops are made only in the largest piece of the slice a Run can get round: the start
        // snaps into it, and Anchors come from it, so every Path between them exists.
        var junctions = LargestUsablePiece(slice);
        if (junctions.Length == 0) return None();
        var start = Nearest(slice, junctions, request.Start);
        if (slice.Node(start).DistanceTo(request.Start) > Request.SnapMarginM) return None();

        // Anchors only where three or more usable ways meet: one at a dead end would leave the
        // Loop a Spur to nothing (R2.6).
        int[] throughRoutes = [.. junctions.Where(n => UsableSegmentsAt(slice, n) >= 3)];
        if (throughRoutes.Length < 2) return None();

        // Placed once for the Request, round the edge of the piece its Loops are made in, under the
        // least each Segment can cost however much ground they use, so they guide every Path. Only
        // for long Loops: placing them made a 10 km Run 0.1 s slower (T068, 2026-11-05).
        var landmarks = request.DistanceKm >= Request.LongFromKm ? Landmarks.Place(slice, junctions, LandmarksPlaced, RunCostModel.Cheapest(leaning), cancellationToken) : null;

        // A search per thread, each the size of the slice, kept for the next batch of circles.
        var searches = new ConcurrentBag<PathSearch>();
        var anchors = new AnchorPlacement(slice, throughRoutes);
        var destinations = DestinationLoops.Plan(slice, start, distanceM, leaning);
        var tally = new Tally(slice, request, weights, progress, Shapes.Sum(s => s.Bearings * (1 + CorrectedScales.Length)) + destinations.Count);
        var candidates = new List<Loop>();
        foreach (var shape in Shapes)
        {
            var firstRadius = distanceM / (FirstDetourGuess * shape.PerimeterPerRadius);
            var first = OnCircles(slice, searches, [.. BearingsRound(shape).Select(b => (b, firstRadius))],
                (search, circle) => tally.Tried(Candidate(slice, search, anchors, shape, start, circle.Bearing, circle.Radius, leaning, landmarks, cancellationToken)), cancellationToken);
            if (first.Count == 0)
            {
                // No first Loops on this shape's circles, so none corrected.
                tally.WillTry(-shape.Bearings * CorrectedScales.Length);
                continue;
            }
            candidates.AddRange(first);

            var detour = Median(first.Select(l => LengthOf(slice, l) / (firstRadius * shape.PerimeterPerRadius)));
            var corrected = distanceM / (detour * shape.PerimeterPerRadius);
            candidates.AddRange(OnCircles(slice, searches, [.. CorrectedScales.SelectMany(scale => BearingsRound(shape).Select(b => (b, corrected * scale)))],
                (search, circle) => tally.Tried(Candidate(slice, search, anchors, shape, start, circle.Bearing, circle.Radius, leaning, landmarks, cancellationToken)), cancellationToken));
        }
        candidates.AddRange(DestinationLoops.Build(slice, start, destinations, leaning, loop => tally.Tried(loop), landmarks, cancellationToken));
        // Each with its spurs cut out too (PLAN T042), if it could still be near enough the
        // Distance: cutting only shortens a Loop.
        candidates.AddRange([.. candidates.Where(l => LengthOf(slice, l) > request.ShortestKm * 1000)
            .Select(l => (Loop: l, Trimmed: Spurs.Trim(slice, l))).Where(p => p.Trimmed != p.Loop).Select(p => p.Trimmed)]);

        return new FoundLoops(slice, weights,
            [.. candidates.Where(l => LengthOf(slice, l) >= request.ShortestKm * 1000 && LengthOf(slice, l) <= request.LongestKm * 1000)]);
    }

    // Counts the Loops tried, keeps those in the length window, and reports each, one at a time.
    private sealed class Tally(NetworkSlice slice, Request request, ScoringWeights weights, Action<SearchProgress>? progress, int toTry)
    {
        private readonly List<Loop> _inWindow = [];
        private int _tried;
        private int _toTry = toTry;

        public void WillTry(int more)
        {
            lock (_inWindow) _toTry += more;
        }

        public Loop? Tried(Loop? loop)
        {
            if (progress is null) return loop;
            lock (_inWindow)
            {
                _tried++;
                if (loop is not null && LengthOf(slice, loop) >= request.ShortestKm * 1000 && LengthOf(slice, loop) <= request.LongestKm * 1000) _inWindow.Add(loop);
                progress(new SearchProgress(_tried, _toTry, loop, new FoundLoops(slice, weights, [.. _inWindow])));
            }
            return loop;
        }
    }

    /// <summary>
    /// The Loops on these circles, built in parallel, a search per thread (review after T030; T054):
    /// in the circles' order, whichever finishes first, so the Loops found don't depend on threads.
    /// </summary>
    private static List<Loop> OnCircles(
        NetworkSlice slice, ConcurrentBag<PathSearch> searches, (double Bearing, double Radius)[] circles,
        Func<PathSearch, (double Bearing, double Radius), Loop?> build, CancellationToken cancellationToken)
    {
        var loops = new Loop?[circles.Length];
        Parallel.For(0, circles.Length, new ParallelOptions { CancellationToken = cancellationToken },
            () => searches.TryTake(out var search) ? search : new PathSearch(slice),
            (i, _, search) =>
            {
                loops[i] = build(search, circles[i]);
                return search;
            },
            searches.Add);
        return [.. loops.OfType<Loop>()];
    }

    private static IEnumerable<double> BearingsRound(Shape shape) => Enumerable.Range(0, shape.Bearings).Select(i => i * 360.0 / shape.Bearings);

    /// <summary>The Loop through the shape's Anchors on the circle of this radius whose centre lies on this bearing from the start.</summary>
    private static Loop? Candidate(
        NetworkSlice slice, PathSearch search, AnchorPlacement placement, Shape shape, int start, double bearingDegrees, double radius,
        ScoringWeights weights, Landmarks? landmarks, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var home = slice.Node(start);
        var bearing = bearingDegrees * Math.PI / 180;
        var centre = new NodePoint(home.X + (radius * Math.Sin(bearing)), home.Y + (radius * Math.Cos(bearing)));
        int[] anchors = [.. shape.AnchorTurns.Select(turn =>
        {
            var angle = bearing + Math.PI + (turn * Math.PI / 180);
            return placement.Near(new NodePoint(centre.X + (radius * Math.Sin(angle)), centre.Y + (radius * Math.Cos(angle))));
        })];
        if (anchors.Contains(start) || anchors.Distinct().Count() != anchors.Length) return null;

        int[] stops = [start, .. anchors, start];
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

    private static double LengthOf(NetworkSlice slice, Loop loop) => loop.Traversals.Sum(t => slice.Segments[t.Segment].LengthM);

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    internal static int UsableSegmentsAt(NetworkSlice slice, int junction)
    {
        var usable = 0;
        foreach (var s in slice.SegmentsAt(junction))
        {
            if (slice.Segments[s].Access != AccessKind.Forbidden) usable++;
        }
        return usable;
    }

    // The junction nearest a point; of a split junction's corners (T145c), the one on the point's side.
    internal static int Nearest(NetworkSlice slice, int[] junctions, NodePoint point)
    {
        var nearest = junctions.MinBy(n => slice.Node(n).DistanceTo(point));
        var corner = Corners.Toward(slice, nearest, point);
        return Array.BinarySearch(junctions, corner) >= 0 ? corner : nearest;
    }

    /// <summary>
    /// The junctions of the largest piece of the slice joined by Segments a Run may use, in
    /// ascending order. Ties go to the piece with the lowest-numbered junction.
    /// </summary>
    internal static int[] LargestUsablePiece(NetworkSlice slice)
    {
        var piece = new int[slice.NodeCount];
        Array.Fill(piece, -1);
        var sizes = new List<int>();
        var stack = new Stack<int>();
        for (var first = 0; first < slice.NodeCount; first++)
        {
            if (piece[first] >= 0) continue;
            var id = sizes.Count;
            var size = 0;
            piece[first] = id;
            stack.Push(first);
            while (stack.TryPop(out var node))
            {
                size++;
                foreach (var s in slice.SegmentsAt(node))
                {
                    var segment = slice.Segments[s];
                    if (segment.Access == AccessKind.Forbidden) continue;
                    var next = segment.From == node ? segment.To : segment.From;
                    if (piece[next] >= 0) continue;
                    piece[next] = id;
                    stack.Push(next);
                }
            }
            sizes.Add(size);
        }
        if (sizes.Count == 0) return [];
        var largest = sizes.IndexOf(sizes.Max());
        return [.. Enumerable.Range(0, slice.NodeCount).Where(n => piece[n] == largest)];
    }
}
