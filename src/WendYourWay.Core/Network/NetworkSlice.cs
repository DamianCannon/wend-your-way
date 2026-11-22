namespace WendYourWay.Core.Network;

/// <summary>
/// The part of the Network a Request needs, held in memory for routing (ADR 0006): junctions,
/// the Segments between them, and which Segments meet at each junction.
/// </summary>
public sealed class NetworkSlice
{
    private readonly NodePoint[] _nodes;
    private readonly Segment[] _segments;
    private readonly int[] _incidentStart;   // compressed adjacency: Segments at node n are
    private readonly int[] _incident;        // _incident[_incidentStart[n] .. _incidentStart[n + 1]]

    public NetworkSlice(IReadOnlyList<NodePoint> nodes, IReadOnlyList<Segment> segments)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(segments);
        _nodes = [.. nodes];
        _segments = [.. segments];
        _offRoadNetworks = new Lazy<OffRoadNetworks>(() => Network.OffRoadNetworks.Of(this));

        var degree = new int[_nodes.Length + 1];
        foreach (var s in _segments)
        {
            if ((uint)s.From >= (uint)_nodes.Length || (uint)s.To >= (uint)_nodes.Length)
                throw new ArgumentException($"Segment {s.Key} joins a node outside the slice.", nameof(segments));
            if (!(s.LengthM > 0) || double.IsInfinity(s.LengthM))
                throw new ArgumentException($"Segment {s.Key} has length {s.LengthM} m; lengths must be positive.", nameof(segments));

            // A* relies on no Segment being shorter than the straight line between its junctions.
            var straight = _nodes[s.From].DistanceTo(_nodes[s.To]);
            if (s.LengthM < straight - 0.01 - (straight * 1e-9))
                throw new ArgumentException(
                    $"Segment {s.Key} is {s.LengthM} m long, shorter than the {straight:0.##} m straight line between its junctions.",
                    nameof(segments));
            degree[s.From]++;
            if (s.To != s.From) degree[s.To]++;
        }

        _incidentStart = new int[_nodes.Length + 1];
        for (var n = 0; n < _nodes.Length; n++) _incidentStart[n + 1] = _incidentStart[n] + degree[n];
        _incident = new int[_incidentStart[^1]];
        var fill = (int[])_incidentStart.Clone();
        for (var i = 0; i < _segments.Length; i++)
        {
            _incident[fill[_segments[i].From]++] = i;
            if (_segments[i].To != _segments[i].From) _incident[fill[_segments[i].To]++] = i;
        }
    }

    /// <summary>
    /// Numbers stored Segments and their junctions into a slice, the same way whatever order they
    /// arrive in: Segments by OSM identity, junctions by ascending OSM node ID. The Path finder
    /// breaks exact ties by junction number, so numbering in arrival order would let the order of
    /// a query's rows choose between equally cheap Paths.
    /// </summary>
    // Less of a Segment than this left over beside its roads isn't worth a group of its own.
    private const double OwnGroundToleranceM = 0.5;

    /// <param name="crossingPoints">
    /// Given (a Run's, T145c), its junctions on Main Roads are split into corners (<see cref="Corners"/>),
    /// crossed at these crossing points, by OSM node; not given, they aren't.
    /// </param>
    public static NetworkSlice Assemble(IEnumerable<StoredSegment> stored, IReadOnlyDictionary<long, CrossingKind>? crossingPoints = null)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var rows = stored
            .OrderBy(s => s.Key.OsmWayId).ThenBy(s => s.Key.FromNodeId).ThenBy(s => s.Key.ToNodeId)
            .ToList();

        // Each junction's position comes from the first Segment, in that order, that ends there.
        var positions = new Dictionary<long, NodePoint>();
        foreach (var row in rows)
        {
            positions.TryAdd(row.Key.FromNodeId, row.FromPoint);
            positions.TryAdd(row.Key.ToNodeId, row.ToPoint);
        }
        var nodeIds = positions.Keys.Order().ToArray();
        var index = new Dictionary<long, int>(nodeIds.Length);
        for (var n = 0; n < nodeIds.Length; n++) index[nodeIds[n]] = n;

        // Each Segment's own ground is a same-ground group named by its identity, numbered in row
        // order. Metres beside a road lie on the road's group, even when the road itself is
        // outside the slice; the rest of the Segment lies on its own.
        var groups = new Dictionary<SegmentKey, int>();
        int GroupOf(SegmentKey key) => groups.TryGetValue(key, out var numbered) ? numbered : groups[key] = groups.Count;
        var segments = new List<Segment>(rows.Count);
        foreach (var s in rows)
        {
            var beside = s.Beside ?? [];
            var own = s.LengthM - beside.Sum(b => b.Metres);
            List<GroundShare> shares = [.. beside.Select(b => new GroundShare(GroupOf(b.Road), b.Metres))];
            if (beside.Count == 0 || own > OwnGroundToleranceM) shares.Add(new GroundShare(GroupOf(s.Key), Math.Max(own, 0)));
            segments.Add(new Segment(s.Key, index[s.Key.FromNodeId], index[s.Key.ToNodeId], s.LengthM, s.Ground, s.Access, shares, s.MainRoad, s.Greenery, s.Corridor, s.Waterside, s.Crossing, s.CrossesMainRoad, s.CrossingShare, s.BridgeOverWater, s.AscentM, s.DescentM)
            {
                Obstacles = s.Obstacles,
                Carriageway = s.Carriageway,
                Pavement = s.Pavement,
                Customers = s.Customers,
                Busy = s.Busy,
                FromBearing = s.FromBearing,
                ToBearing = s.ToBearing,
                MapGap = s.MapGap,
            });
        }
        var slice = new NetworkSlice([.. nodeIds.Select(id => positions[id])], segments);
        if (crossingPoints is null) return slice;
        var points = new Dictionary<int, CrossingKind>();
        foreach (var (node, kind) in crossingPoints)
        {
            if (index.TryGetValue(node, out var n)) points[n] = kind;
        }
        return Corners.Split(slice, points);
    }

    /// <summary>
    /// Each corner of a split junction (T145c), by node: the bearing it starts at, clockwise from grid north,
    /// and how many degrees it spans clockwise from there, between the carriageway arms either side.
    /// </summary>
    internal IReadOnlyDictionary<int, (double From, double Span)>? CornerSectors { get; init; }

    public int NodeCount => _nodes.Length;

    /// <summary>The slice's Off-road networks, worked out once, when first asked for (T047).</summary>
    public OffRoadNetworks OffRoadNetworks => _offRoadNetworks.Value;

    private readonly Lazy<OffRoadNetworks> _offRoadNetworks;

    public IReadOnlyList<Segment> Segments => _segments;

    public NodePoint Node(int index) => _nodes[index];

    /// <summary>Indices of the Segments that meet at a junction, in a fixed order.</summary>
    public ReadOnlySpan<int> SegmentsAt(int node) =>
        _incident.AsSpan(_incidentStart[node], _incidentStart[node + 1] - _incidentStart[node]);
}
