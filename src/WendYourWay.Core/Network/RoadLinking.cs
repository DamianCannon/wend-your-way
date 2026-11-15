namespace WendYourWay.Core.Network;

/// <summary>
/// Gravel Off-road networks joined through road links (PLAN T074): two are one where a route along
/// the roads given joins a junction of one to a junction of the other within the link, however many
/// Segments it takes. One search outwards from every network at once labels each road junction with
/// its nearest network and how far it is; where a road's two ends are labelled with different
/// networks, and the route through it is within the link, those networks join.
/// </summary>
public static class RoadLinking
{
    /// <summary>The longest road link (T074): 742 hotspots across Great Britain, 120 near London.</summary>
    public const double LinkM = 1000;

    /// <summary>
    /// For each of <paramref name="networks"/> networks, the lowest-numbered network it is joined to
    /// (itself if none), by <paramref name="roads"/> no longer than <paramref name="linkM"/> from end to end.
    /// </summary>
    /// <param name="networkAt">Each junction of a network, by OSM node id, and its network.</param>
    /// <param name="roads">The roads a link may use, by OSM node ids at each end, and their metres.</param>
    public static int[] Join(int networks, IReadOnlyDictionary<long, int> networkAt, IReadOnlyList<(long A, long B, double M)> roads, double linkM = LinkM)
    {
        ArgumentNullException.ThrowIfNull(networkAt);
        ArgumentNullException.ThrowIfNull(roads);

        // The roads' junctions numbered, and the roads at each in compressed adjacency, as a slice's are:
        // millions of them across Great Britain.
        var number = new Dictionary<long, int>();
        int Junction(long id) => number.TryGetValue(id, out var n) ? n : number[id] = number.Count;
        var ends = new (int A, int B)[roads.Count];
        for (var i = 0; i < roads.Count; i++) ends[i] = (Junction(roads[i].A), Junction(roads[i].B));
        var start = new int[number.Count + 1];
        foreach (var (a, b) in ends)
        {
            start[a + 1]++;
            start[b + 1]++;
        }
        for (var n = 0; n < number.Count; n++) start[n + 1] += start[n];
        var at = new int[start[^1]];
        var fill = (int[])start.Clone();
        for (var i = 0; i < ends.Length; i++)
        {
            at[fill[ends[i].A]++] = i;
            at[fill[ends[i].B]++] = i;
        }

        // Outwards from every network's junctions at once, no farther than the link.
        var network = new int[number.Count];
        var distance = new double[number.Count];
        Array.Fill(network, -1);
        Array.Fill(distance, double.PositiveInfinity);
        var open = new PriorityQueue<int, double>();
        foreach (var (id, n) in networkAt)
        {
            if (!number.TryGetValue(id, out var junction)) continue;
            (network[junction], distance[junction]) = (n, 0);
            open.Enqueue(junction, 0);
        }
        var settled = new bool[number.Count];
        while (open.TryDequeue(out var junction, out _))
        {
            if (settled[junction]) continue;
            settled[junction] = true;
            for (var k = start[junction]; k < start[junction + 1]; k++)
            {
                var road = at[k];
                var next = ends[road].A == junction ? ends[road].B : ends[road].A;
                var reached = distance[junction] + roads[road].M;
                if (reached > linkM || reached >= distance[next]) continue;
                (network[next], distance[next]) = (network[junction], reached);
                open.Enqueue(next, reached);
            }
        }

        // Where two networks' reaches meet along a road, within the link from end to end, they join.
        var parent = Enumerable.Range(0, networks).ToArray();
        int Find(int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }
        for (var i = 0; i < ends.Length; i++)
        {
            var (a, b) = ends[i];
            if (network[a] < 0 || network[b] < 0 || network[a] == network[b]) continue;
            if (distance[a] + roads[i].M + distance[b] > linkM) continue;
            var (ra, rb) = (Find(network[a]), Find(network[b]));
            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
        return [.. Enumerable.Range(0, networks).Select(Find)];
    }
}
