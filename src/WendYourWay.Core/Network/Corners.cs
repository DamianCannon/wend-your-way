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
                    arm.Access == AccessKind.Forbidden ? AccessKind.Forbidden : AccessKind.Allowed,
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
        return new NetworkSlice(nodes, split);
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
