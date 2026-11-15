namespace WendYourWay.Core.Network;

/// <summary>
/// Off-road networks (CONTEXT.md): green ground joined at its junctions and across single road
/// Segments of up to <see cref="BridgedRoadM"/>. The one definition of green ground and its bridge,
/// for Flow, Anchors and where to start (T022), after the review after T037.
/// </summary>
public sealed class OffRoadNetworks
{
    /// <summary>A road link this short between green ground doesn't break it (a crossing, a bridge).</summary>
    public const double BridgedRoadM = 100;

    private readonly int[] _network;
    private readonly double[] _metres;

    private OffRoadNetworks(int[] network, double[] metres)
    {
        _network = network;
        _metres = metres;
    }

    /// <summary>Green ground: Off-road or Traffic-free, and open to a Run.</summary>
    public static bool IsGreen(Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Access != AccessKind.Forbidden && segment.Ground is GroundKind.OffRoad or GroundKind.TrafficFree;
    }

    /// <summary>The metres of green ground in the Off-road network at a junction: 0 if it touches none.</summary>
    public double MetresAt(int junction) => _network[junction] >= 0 ? _metres[_network[junction]] : 0;

    /// <summary>The Off-road network a junction is in, numbered from 0; -1 if it touches no green ground.</summary>
    public int NetworkAt(int junction) => _network[junction];

    /// <summary>The metres of green ground in an Off-road network.</summary>
    public double MetresOf(int network) => _metres[network];

    /// <summary>The slice's Off-road networks.</summary>
    public static OffRoadNetworks Of(NetworkSlice slice)
    {
        ArgumentNullException.ThrowIfNull(slice);
        var parent = Enumerable.Range(0, slice.NodeCount).ToArray();
        int Find(int n)
        {
            while (parent[n] != n) n = parent[n] = parent[parent[n]];
            return n;
        }
        void Union(int a, int b) => parent[Find(a)] = Find(b);

        var touchesGreen = new bool[slice.NodeCount];
        foreach (var segment in slice.Segments.Where(IsGreen))
        {
            touchesGreen[segment.From] = touchesGreen[segment.To] = true;
            Union(segment.From, segment.To);
        }

        // Across a single road Segment between green ground, not a chain of them: in a city's grid of
        // streets, 100 m of road in any direction joins pocket squares into one network that draws
        // Anchors, and Trafalgar Square's Loop took 1 km more road (2026-10-23). Flow bridges a
        // road run along one Loop, which can't branch.
        foreach (var segment in slice.Segments)
        {
            if (!IsGreen(segment) && segment.Access != AccessKind.Forbidden && segment.LengthM <= BridgedRoadM
                && touchesGreen[segment.From] && touchesGreen[segment.To])
                Union(segment.From, segment.To);
        }

        var index = new Dictionary<int, int>();
        var metresOf = new List<double>();
        foreach (var segment in slice.Segments.Where(IsGreen))
        {
            var root = Find(segment.From);
            if (!index.TryGetValue(root, out var i))
            {
                index[root] = i = metresOf.Count;
                metresOf.Add(0);
            }
            metresOf[i] += segment.LengthM;
        }
        var network = new int[slice.NodeCount];
        for (var n = 0; n < slice.NodeCount; n++) network[n] = touchesGreen[n] ? index[Find(n)] : -1;
        return new OffRoadNetworks(network, [.. metresOf]);
    }
}
