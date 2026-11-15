namespace WendYourWay.Core.Network;

/// <summary>
/// <see cref="OffRoadNetworks"/> over Segments as stored, by OSM node id, for the Network build
/// (PLAN T022): too many for a slice, so they're streamed in, every green Segment first and then
/// the roads that might join them. The same rule as <see cref="OffRoadNetworks.Of"/>.
/// </summary>
public sealed class OffRoadJoining
{
    // Junctions numbered as green Segments first reach them; a road between two of them is the
    // only kind that can join networks, so nothing else is numbered.
    private readonly Dictionary<long, int> _junctions = [];
    private readonly List<int> _parent = [];
    private readonly List<(int From, int To, double LengthM)> _green = [];
    private bool _roads;

    /// <summary>Adds a green Segment: Off-road or Traffic-free, and open to a Run.</summary>
    public void AddGreen(long from, long to, double lengthM)
    {
        if (_roads) throw new InvalidOperationException("Every green Segment comes before the roads.");
        var (a, b) = (Junction(from), Junction(to));
        Union(a, b);
        _green.Add((a, b, lengthM));
    }

    /// <summary>
    /// Adds a road Segment open to a Run: one of up to <see cref="OffRoadNetworks.BridgedRoadM"/>
    /// between green ground at both ends joins the networks there.
    /// </summary>
    public void AddRoad(long from, long to, double lengthM)
    {
        _roads = true;
        if (lengthM <= OffRoadNetworks.BridgedRoadM && _junctions.TryGetValue(from, out var a) && _junctions.TryGetValue(to, out var b))
            Union(a, b);
    }

    /// <summary>The networks found: each green Segment's, in the order added, and each network's metres.</summary>
    public Joined Finish()
    {
        var index = new Dictionary<int, int>();
        var metres = new List<double>();
        var networkOf = new int[_green.Count];
        for (var i = 0; i < _green.Count; i++)
        {
            var root = Find(_green[i].From);
            if (!index.TryGetValue(root, out var network))
            {
                index[root] = network = metres.Count;
                metres.Add(0);
            }
            metres[network] += _green[i].LengthM;
            networkOf[i] = network;
        }
        return new Joined(networkOf, [.. metres]);
    }

    /// <summary>The networks: each green Segment's, numbered from 0 in the order added, and each one's metres of green ground.</summary>
    public sealed record Joined(int[] NetworkOf, double[] Metres);

    private int Junction(long id)
    {
        if (_junctions.TryGetValue(id, out var junction)) return junction;
        _junctions[id] = junction = _parent.Count;
        _parent.Add(junction);
        return junction;
    }

    private int Find(int n)
    {
        while (_parent[n] != n) n = _parent[n] = _parent[_parent[n]];
        return n;
    }

    private void Union(int a, int b) => _parent[Find(a)] = Find(b);
}
