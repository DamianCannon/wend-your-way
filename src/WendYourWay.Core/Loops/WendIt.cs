using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Loops;

/// <summary>A route wended (T194): the Loop, the road it saves on the route's, and the share of its length on the route.</summary>
public sealed record Variation(Loop Loop, double RoadSavedM, double KeptShare);

/// <summary>
/// Wend It! (T194, #198; STORY-052; user: "a 'Wend It!' button that comes up with variations on the route"): a route
/// found again through its turning points, from its start, ground already used made dearer, as a destination Loop is.
/// The user chose "Its shape": its start, turning points and about its length kept. Not trimmed of spurs: a usual run
/// is often out and back, and trimmed, one of 10.9 km came to 3.5 (the T192 spike, #196, which found this way better
/// the road on 5 of 8 of the user's usual runs; avoiding each stretch of road shrank them, and the generator's own Loops
/// followed few).
/// </summary>
public static class WendIt
{
    /// <summary>How near the route a variation's ground is to be on it.</summary>
    public const double OnTheRouteM = 50;

    /// <summary>The least share of a variation's length on the route: less, and it's another run.</summary>
    public const double KeptAtLeast = 0.5;

    /// <summary>How much longer or shorter than the route a variation may be, as a share of its length.</summary>
    public const double LengthWithin = 0.15;

    /// <summary>How much more Main Road than the route a variation may take: the user's least liked ground.</summary>
    public const double MoreMainRoadM = 50;

    /// <summary>
    /// The most ground two variations may share, as a share of either's Segments: each keeps half or more of the same
    /// route, so not the 30% two Loops offered may (found on Richmond Park: five, each saving 83 m).
    /// </summary>
    public const double MostAlike = 0.9;

    /// <summary>
    /// The variations of <paramref name="route"/> with less road, most road saved first, none the same; none if there's no
    /// better way. Found through 3 of its turning points, 8, and one a km, at the Off-road Lean's centre and leaning to
    /// trails: fewer turning points leave more room, more keep closer to the route.
    /// </summary>
    public static IReadOnlyList<Variation> Of(NetworkSlice slice, Loop route, ScoringWeights weights, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(weights);
        if (route.Traversals.Count == 0) return [];

        var nodes = JunctionsOf(slice, route);
        var line = nodes.Select(slice.Node).ToArray();
        var was = Breakdown.Of(slice, route);
        var search = new PathSearch(slice);
        var found = new List<Variation>();
        var seen = new HashSet<string>();
        foreach (var turns in new[] { 3, 8, (int)Math.Round(was.LengthM / 1000) }.Distinct())
        {
            var stops = TurningPoints(slice, nodes, turns);
            foreach (var lean in new[] { 0.0, 1.0 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Through(slice, search, route.Start, stops, weights with { OffRoadLean = lean }) is not { } loop) continue;
                if (!seen.Add(string.Join(',', loop.Traversals.Select(t => t.Segment)))) continue;
                var now = Breakdown.Of(slice, loop);
                var kept = KeptOf(slice, loop, line);
                if (now.ConnectorLengthM < was.ConnectorLengthM
                    && now.MainRoadLengthM <= was.MainRoadLengthM + MoreMainRoadM
                    && Math.Abs(now.LengthM - was.LengthM) <= LengthWithin * was.LengthM
                    && kept >= KeptAtLeast)
                    found.Add(new Variation(loop, was.ConnectorLengthM - now.ConnectorLengthM, kept));
            }
        }
        // Most road saved first, then each that isn't one already kept over again.
        var offered = new List<(Variation Variation, HashSet<int> Ground)>();
        foreach (var variation in found.OrderByDescending(v => v.RoadSavedM))
        {
            var ground = variation.Loop.Traversals.Select(t => t.Segment).ToHashSet();
            if (offered.All(o => o.Ground.Intersect(ground).Count() <= MostAlike * Math.Min(o.Ground.Count, ground.Count))) offered.Add((variation, ground));
        }
        return [.. offered.Select(o => o.Variation)];
    }

    // Its junctions in travel order, the start first.
    private static List<int> JunctionsOf(NetworkSlice slice, Loop loop)
    {
        var nodes = new List<int> { loop.Start };
        foreach (var t in loop.Traversals)
        {
            var segment = slice.Segments[t.Segment];
            nodes.Add(t.Forward ? segment.To : segment.From);
        }
        return nodes;
    }

    // Up to `count` of its junctions, in travel order: each the furthest from the line through those chosen so far
    // (Douglas–Peucker's way), so the first are where it turns most, as an out-and-back's far end.
    private static int[] TurningPoints(NetworkSlice slice, List<int> nodes, int count)
    {
        var chosen = new SortedSet<int> { 0, nodes.Count - 1 };
        while (chosen.Count < count + 2)
        {
            var (best, furthest) = (-1, 0.0);
            var ends = chosen.ToArray();
            for (var c = 0; c + 1 < ends.Length; c++)
            {
                for (var i = ends[c] + 1; i < ends[c + 1]; i++)
                {
                    var m = ToChord(slice.Node(nodes[i]), slice.Node(nodes[ends[c]]), slice.Node(nodes[ends[c + 1]]));
                    if (m > furthest) (best, furthest) = (i, m);
                }
            }
            if (best < 0) break;
            chosen.Add(best);
        }
        return [.. chosen.Where(i => i != 0 && i != nodes.Count - 1).Select(i => nodes[i])];
    }

    // From the start through each stop and back, each Path the router's, ground already used dearer; none if a stop
    // can't be reached.
    private static Loop? Through(NetworkSlice slice, PathSearch search, int start, int[] stops, ScoringWeights weights)
    {
        int[] all = [start, .. stops, start];
        var used = new HashSet<int>();
        var traversals = new List<Traversal>();
        for (var i = 0; i + 1 < all.Length; i++)
        {
            if (all[i] == all[i + 1]) continue;
            var path = search.Find(PathEnd.AtNode(all[i]), PathEnd.AtNode(all[i + 1]), new RunCostModel(weights, used));
            if (path is null) return null;
            traversals.AddRange(path.Traversals);
            foreach (var t in path.Traversals) slice.Segments[t.Segment].Cover(used);
        }
        return new Loop(start, stops, traversals);
    }

    // The share of its length within OnTheRouteM of the route's line, each Segment by its middle.
    private static double KeptOf(NetworkSlice slice, Loop loop, NodePoint[] route)
    {
        double on = 0, all = 0;
        foreach (var t in loop.Traversals)
        {
            var segment = slice.Segments[t.Segment];
            if (segment.IsCrossingLink) continue;
            var (from, to) = (slice.Node(segment.From), slice.Node(segment.To));
            var middle = new NodePoint((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            all += segment.LengthM;
            for (var i = 1; i < route.Length; i++)
            {
                if (ToChord(middle, route[i - 1], route[i]) > OnTheRouteM) continue;
                on += segment.LengthM;
                break;
            }
        }
        return all > 0 ? on / all : 0;
    }

    private static double ToChord(NodePoint p, NodePoint a, NodePoint b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var along = (dx * dx) + (dy * dy) == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / ((dx * dx) + (dy * dy)), 0, 1);
        return p.DistanceTo(new NodePoint(a.X + (along * dx), a.Y + (along * dy)));
    }
}
