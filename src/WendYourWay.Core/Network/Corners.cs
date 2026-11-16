using WendYourWay.Core.Routing;

namespace WendYourWay.Core.Network;

/// <summary>
/// Main Roads crossed at real crossings (#149, T145b). Each junction on a Main Road's carriageway is
/// split into its corners: the carriageway's arms there divide the ground round it, two corners
/// mid-road, three or four where Main Roads meet, and a split road's middle a corner of its own.
/// Every other Segment there joins the corner it leaves into; a carriageway Segment is one copy for
/// each side, joining that side's corners at its ends (going along it on one side crosses nothing);
/// and a crossing link joins the corners either side of each arm, charged as that crossing is: a
/// crossing point mapped on the road there, or a crossing way meeting it, which then isn't charged
/// itself; otherwise unmarked. All corners of a junction stand where it does.
/// </summary>
public static class Corners
{
    /// <summary>How long a crossing link is: about a road's width.</summary>
    public const double LinkM = 10;

    /// <summary>A busy road's, wider.</summary>
    public const double BusyLinkM = 15;

    /// <summary>
    /// <paramref name="slice"/> with its carriageway junctions split into corners; the slice itself if
    /// it has no carriageway (a Network built before T145).
    /// </summary>
    /// <param name="crossingPoints">The kind of crossing point mapped at a junction, by node index.</param>
    public static NetworkSlice Split(NetworkSlice slice, IReadOnlyDictionary<int, CrossingKind>? crossingPoints = null)
    {
        ArgumentNullException.ThrowIfNull(slice);
        crossingPoints ??= new Dictionary<int, CrossingKind>();
        var segments = slice.Segments;
        if (!segments.Any(s => s.Carriageway)) return slice;

        var nodes = new List<NodePoint>(slice.NodeCount);
        for (var n = 0; n < slice.NodeCount; n++) nodes.Add(slice.Node(n));

        // Each junction's corners: for each carriageway arm in clockwise order, the corner after it
        // (clockwise) and before it; for each other Segment end, its corner.
        var cornerOfEnd = new Dictionary<(int Segment, bool AtFrom), int>();
        var afterArm = new Dictionary<(int Segment, bool AtFrom), int>();
        var beforeArm = new Dictionary<(int Segment, bool AtFrom), int>();
        var links = new List<Segment>();
        var cleared = new HashSet<int>();     // crossing ways whose charge a link takes
        var nextGroup = segments.SelectMany(s => s.SameGround).Select(g => g.Group).DefaultIfEmpty(-1).Max() + 1;
        var sectors = new Dictionary<int, (double From, double Span)>();

        for (var n = 0; n < slice.NodeCount; n++)
        {
            var ends = new List<(int Segment, bool AtFrom, double Bearing, bool Arm)>();
            foreach (var s in slice.SegmentsAt(n))
            {
                var segment = segments[s];
                if (segment.From == n) ends.Add((s, true, BearingAt(slice, segment, atFrom: true), segment.Carriageway));
                if (segment.To == n) ends.Add((s, false, BearingAt(slice, segment, atFrom: false), segment.Carriageway));
            }
            var arms = ends.Where(e => e.Arm).OrderBy(e => e.Bearing).ThenBy(e => e.Segment).ThenBy(e => e.AtFrom).ToList();
            if (arms.Count == 0) continue;

            // Corner i lies clockwise after arm i, up to arm i + 1; the first is the junction itself.
            var corners = new int[arms.Count];
            corners[0] = n;
            for (var i = 1; i < arms.Count; i++)
            {
                corners[i] = nodes.Count;
                nodes.Add(slice.Node(n));
            }
            int CornerOf(double bearing)
            {
                if (arms.Count == 1) return corners[0];
                for (var i = 0; i < arms.Count; i++)
                {
                    var from = arms[i].Bearing;
                    var to = arms[(i + 1) % arms.Count].Bearing;
                    var span = Clockwise(from, to);
                    if (span == 0) span = 360;
                    var at = Clockwise(from, bearing);
                    if (at > 0 && at < span) return corners[i];
                }
                return corners[0];
            }

            foreach (var end in ends.Where(e => !e.Arm)) cornerOfEnd[(end.Segment, end.AtFrom)] = CornerOf(end.Bearing);
            for (var i = 0; i < arms.Count; i++)
            {
                afterArm[(arms[i].Segment, arms[i].AtFrom)] = corners[i];
                beforeArm[(arms[i].Segment, arms[i].AtFrom)] = corners[(i - 1 + arms.Count) % arms.Count];
            }
            if (arms.Count < 2) continue;
            for (var i = 0; i < arms.Count; i++)
            {
                var span = Clockwise(arms[i].Bearing, arms[(i + 1) % arms.Count].Bearing);
                sectors[corners[i]] = (arms[i].Bearing, span == 0 ? 360 : span);
            }

            // What crossing here is: a crossing point on the road, or a crossing way meeting it.
            CrossingKind? kind = crossingPoints.TryGetValue(n, out var point) ? point : null;
            foreach (var end in ends.Where(e => !e.Arm && segments[e.Segment].Crossing is not null && segments[e.Segment].CrossesMainRoad))
            {
                kind ??= segments[end.Segment].Crossing;
                cleared.Add(end.Segment);
            }
            for (var i = 0; i < arms.Count; i++)
            {
                var arm = segments[arms[i].Segment];
                var nodeId = arms[i].AtFrom ? arm.Key.FromNodeId : arm.Key.ToNodeId;
                links.Add(new Segment(
                    new SegmentKey(-Math.Abs(arm.Key.OsmWayId), nodeId, -(i + 1)),
                    corners[(i - 1 + arms.Count) % arms.Count], corners[i],
                    arm.Busy ? BusyLinkM : LinkM, GroundKind.Connector,
                    // A road no one may walk along (foot=no, a motorway) is crossed only where a crossing is.
                    arm.Access == AccessKind.Forbidden && kind is null ? AccessKind.Forbidden : AccessKind.Allowed,
                    nextGroup++, Crossing: kind ?? CrossingKind.Unmarked, CrossesMainRoad: true, CrossingShare: 1)
                {
                    Busy = arm.Busy,
                });
            }
        }

        var split = new List<Segment>(segments.Count + links.Count);
        for (var s = 0; s < segments.Count; s++)
        {
            var segment = cleared.Contains(s) ? segments[s] with { Crossing = null, CrossesMainRoad = false, CrossingShare = 0 } : segments[s];
            if (!segment.Carriageway)
            {
                split.Add(segment with
                {
                    From = cornerOfEnd.GetValueOrDefault((s, true), segment.From),
                    To = cornerOfEnd.GetValueOrDefault((s, false), segment.To),
                });
                continue;
            }
            // Its right side, going From to To: after it at From, before it at To; its left the other way.
            split.Add(segment with { From = afterArm[(s, true)], To = beforeArm[(s, false)] });
            split.Add(segment with { From = beforeArm[(s, true)], To = afterArm[(s, false)] });
        }
        split.AddRange(links);
        return new NetworkSlice(nodes, split) { CornerSectors = sectors };
    }

    /// <summary>
    /// A junction's corners (T145c): <paramref name="node"/> and every other at its place joined to it by
    /// crossing links, in order of their numbers; the node alone where nothing was split.
    /// </summary>
    public static IReadOnlyList<int> Of(NetworkSlice slice, int node)
    {
        ArgumentNullException.ThrowIfNull(slice);
        var found = new SortedSet<int> { node };
        var next = new Queue<int>([node]);
        while (next.TryDequeue(out var at))
        {
            foreach (var s in slice.SegmentsAt(at))
            {
                var link = slice.Segments[s];
                if (!link.IsCrossingLink) continue;
                var other = link.From == at ? link.To : link.From;
                if (found.Add(other)) next.Enqueue(other);
            }
        }
        return [.. found];
    }

    /// <summary>
    /// Of <paramref name="node"/>'s corners, the one facing <paramref name="point"/> (T145c): a start beside a
    /// Main Road junction is snapped to its own side, so starting costs no crossing. The node itself where
    /// nothing was split, or the point is on it.
    /// </summary>
    public static int Toward(NetworkSlice slice, int node, NodePoint point)
    {
        ArgumentNullException.ThrowIfNull(slice);
        var here = slice.Node(node);
        if (slice.CornerSectors is not { Count: > 0 } sectors || point == here) return node;
        var bearing = (Math.Atan2(point.X - here.X, point.Y - here.Y) * 180 / Math.PI + 360) % 360;
        foreach (var corner in Of(slice, node))
        {
            if (sectors.TryGetValue(corner, out var sector) && Clockwise(sector.From, bearing) < sector.Span) return corner;
        }
        return node;
    }

    /// <summary>
    /// Traversals as the map has them (T145c), to draw or save: by OSM way, crossing links left out.
    /// </summary>
    public static IEnumerable<(SegmentKey Key, bool Forward)> OnTheMap(NetworkSlice slice, IEnumerable<Traversal> traversals)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(traversals);
        foreach (var t in traversals)
        {
            var segment = slice.Segments[t.Segment];
            if (!segment.IsCrossingLink) yield return (segment.Key, t.Forward);
        }
    }

    // A step between two Traversals that don't join, and couldn't by crossing: a gap in an imported route.
    private const double GapCost = 1e9;

    /// <summary>
    /// A route saved by its OSM ways put back on the slice (T145c): of a carriageway's two sides, the
    /// ones that join up crossing least, and the crossings between them put back; a closed route also
    /// back to the corner it began at. None if a way isn't in the slice: the map has changed under it.
    /// </summary>
    public static IReadOnlyList<Traversal>? Rejoin(NetworkSlice slice, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(traversals);
        if (traversals.Count == 0) return [];
        var copies = new Dictionary<SegmentKey, List<int>>();
        for (var s = 0; s < slice.Segments.Count; s++)
        {
            var key = slice.Segments[s].Key;
            if (!copies.TryGetValue(key, out var list)) copies[key] = list = [];
            list.Add(s);
        }
        var options = new List<Traversal[]>(traversals.Count);
        foreach (var (key, forward) in traversals)
        {
            if (!copies.TryGetValue(key, out var list)) return null;
            options.Add([.. list.Select(s => new Traversal(s, forward))]);
        }
        int StartOf(Traversal t) => t.Forward ? slice.Segments[t.Segment].From : slice.Segments[t.Segment].To;
        int EndOf(Traversal t) => t.Forward ? slice.Segments[t.Segment].To : slice.Segments[t.Segment].From;
        var closed = slice.Node(StartOf(options[0][0])) == slice.Node(EndOf(options[^1][0]));

        // For each choice of the first's side, the cheapest sides after it; the cheapest of those.
        (double Cost, List<Traversal> Route)? best = null;
        foreach (var first in options[0])
        {
            var cost = new double[options.Count][];
            var from = new int[options.Count][];
            cost[0] = [.. options[0].Select(o => o == first ? 0 : double.PositiveInfinity)];
            from[0] = new int[options[0].Length];
            for (var i = 1; i < options.Count; i++)
            {
                cost[i] = new double[options[i].Length];
                from[i] = new int[options[i].Length];
                for (var c = 0; c < options[i].Length; c++)
                {
                    (cost[i][c], from[i][c]) = (double.PositiveInfinity, 0);
                    for (var p = 0; p < options[i - 1].Length; p++)
                    {
                        var step = cost[i - 1][p] + Hop(slice, EndOf(options[i - 1][p]), StartOf(options[i][c])).Cost;
                        if (step < cost[i][c]) (cost[i][c], from[i][c]) = (step, p);
                    }
                }
            }
            var (endCost, end) = (double.PositiveInfinity, 0);
            for (var c = 0; c < options[^1].Length; c++)
            {
                var total = cost[^1][c] + (closed ? Hop(slice, EndOf(options[^1][c]), StartOf(first)).Cost : 0);
                if (total < endCost) (endCost, end) = (total, c);
            }
            if (best is { } kept && kept.Cost <= endCost) continue;

            var chosen = new Traversal[options.Count];
            for (int i = options.Count - 1, c = end; i >= 0; c = from[i][c], i--) chosen[i] = options[i][c];
            var route = new List<Traversal> { chosen[0] };
            for (var i = 1; i < chosen.Length; i++)
            {
                route.AddRange(Hop(slice, EndOf(chosen[i - 1]), StartOf(chosen[i])).Links);
                route.Add(chosen[i]);
            }
            if (closed) route.AddRange(Hop(slice, EndOf(chosen[^1]), StartOf(chosen[0])).Links);
            best = (endCost, route);
        }
        return best!.Value.Route;
    }

    // The cheapest crossing links from one corner of a junction to another, and what they cost; nothing
    // between the same corner; a gap between corners no links join.
    private static (double Cost, IReadOnlyList<Traversal> Links) Hop(NetworkSlice slice, int from, int to)
    {
        if (from == to) return (0, []);
        var costs = new Dictionary<int, (double Cost, Traversal By, int Before)> { [from] = (0, default, -1) };
        var open = new PriorityQueue<int, double>([(from, 0)]);
        while (open.TryDequeue(out var at, out var atCost))
        {
            if (atCost > costs[at].Cost) continue;
            if (at == to)
            {
                var links = new List<Traversal>();
                for (var n = to; n != from; n = costs[n].Before) links.Add(costs[n].By);
                links.Reverse();
                return (atCost, links);
            }
            foreach (var s in slice.SegmentsAt(at))
            {
                var link = slice.Segments[s];
                if (!link.IsCrossingLink) continue;
                var forward = link.From == at;
                var other = forward ? link.To : link.From;
                var cost = atCost + link.LengthM + Scoring.CrossingCosts.Default.Of(link.Crossing ?? CrossingKind.Unmarked, mainRoad: true, link.Busy);
                if (costs.TryGetValue(other, out var known) && known.Cost <= cost) continue;
                costs[other] = (cost, new Traversal(s, forward), at);
                open.Enqueue(other, cost);
            }
        }
        return (GapCost, []);
    }

    // Degrees clockwise from one bearing round to another, 0 up to 360.
    private static double Clockwise(double from, double to) => ((to - from) % 360 + 360) % 360;

    // Which way a Segment leaves a junction: its own bearing there, or the line to its other end.
    private static double BearingAt(NetworkSlice slice, Segment segment, bool atFrom)
    {
        if ((atFrom ? segment.FromBearing : segment.ToBearing) is { } bearing) return bearing;
        var (here, there) = atFrom ? (slice.Node(segment.From), slice.Node(segment.To)) : (slice.Node(segment.To), slice.Node(segment.From));
        var degrees = Math.Atan2(there.X - here.X, there.Y - here.Y) * 180 / Math.PI;
        return (degrees + 360) % 360;
    }
}
