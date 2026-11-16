using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;

namespace WendYourWay.Core.Specs.Support;

/// <summary>
/// Builds a <see cref="NetworkSlice"/> from a small map drawn in ASCII, so specs read like the
/// network they describe.
///
/// <code>
///   S=========T      capital letters and digits are junctions (nodes)
///   :         :      .  :   Off-road        (horizontal, vertical)
///   :         :      -  |   Traffic-free
///   A.........B      =  #   Connector (road)
///                    x      anywhere on a line: Access forbidden for a Run
///                    m      anywhere on a road (= or #): a Main Road
///                    s      anywhere on a line: a stile, an Obstacle (T069)
///                    c      anywhere on a road: a Main Road's carriageway, whose sides are crossed (T145)
///                    b      anywhere on a road: a busy Main Road's carriageway (T145)
/// </code>
///
/// Lines run only horizontally or vertically, between two junctions. One character across is
/// 50 m and one line down is 100 m, which keeps drawings roughly to scale in a monospaced font.
/// A gap (space) means there is no Segment. Lower-case letters are never junctions, so `x` is
/// free to mark forbidden lines.
/// </summary>
internal sealed class AsciiNetwork
{
    public const double MetresAcross = 50;
    public const double MetresDown = 100;

    private readonly Dictionary<char, int> _nodes;
    private readonly Dictionary<(int, int), int> _segmentsByEnds;

    private AsciiNetwork(NetworkSlice slice, Dictionary<char, int> nodes, Dictionary<(int, int), int> segmentsByEnds)
    {
        Slice = slice;
        _nodes = nodes;
        _segmentsByEnds = segmentsByEnds;
    }

    public NetworkSlice Slice { get; }

    /// <summary>The node index of the junction drawn as <paramref name="name"/>.</summary>
    public int Node(char name) =>
        _nodes.TryGetValue(name, out var index) ? index : throw new ArgumentException($"No junction '{name}' on the map.");

    /// <summary>The name a junction is drawn with.</summary>
    public char NameOf(int node) => _nodes.First(kv => kv.Value == node).Key;

    /// <summary>The Segment drawn between two junctions, written "A-B" (either order).</summary>
    public int Segment(string ends)
    {
        var (a, b) = ParseEnds(ends);
        return _segmentsByEnds.TryGetValue(Key(Node(a), Node(b)), out var index)
            ? index
            : throw new ArgumentException($"No Segment {ends} on the map.");
    }

    /// <summary>
    /// The Traversals of a walk through the named junctions in order, e.g. "SABS" for a Loop
    /// from S round A and B and back. Each consecutive pair must be joined by a drawn Segment.
    /// </summary>
    public IReadOnlyList<Traversal> Walk(string junctions)
    {
        var traversals = new List<Traversal>();
        for (var i = 1; i < junctions.Length; i++)
        {
            var segment = Segment($"{junctions[i - 1]}-{junctions[i]}");
            traversals.Add(new Traversal(segment, Slice.Segments[segment].From == Node(junctions[i - 1])));
        }
        return traversals;
    }

    /// <summary>A Loop walked through the named junctions, e.g. "SABS"; it has no Anchors.</summary>
    public Loop LoopThrough(string junctions) => new(Node(junctions[0]), [], Walk(junctions));

    /// <summary>
    /// Parses a map. Each entry of <paramref name="sameGround"/> lists Segments that share one
    /// same-ground group, e.g. "A-B C-D" for two separately mapped pavements of one road.
    /// </summary>
    public static AsciiNetwork Parse(string map, params string[] sameGround)
    {
        var rows = map.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        char At(int r, int c) => c < rows[r].Length ? rows[r][c] : ' ';

        var nodes = new Dictionary<char, int>();
        var points = new List<NodePoint>();
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var ch = rows[r][c];
                if (!IsJunction(ch)) continue;
                if (!nodes.TryAdd(ch, points.Count))
                    throw new ArgumentException($"Junction '{ch}' appears twice on the map.");
                points.Add(new NodePoint(c * MetresAcross, -r * MetresDown));
            }
        }

        var drawn = new List<(int From, int To, double Length, GroundKind Ground, bool Forbidden, bool MainRoad, bool Stile, bool Carriageway, bool Busy)>();
        foreach (var (name, index) in nodes)
        {
            var (r, c) = Locate(rows, name);

            // Horizontal: run right until the next character that isn't an edge character.
            var end = c + 1;
            while (end < rows[r].Length && IsLine(rows[r][end])) end++;
            if (end > c + 1 && end < rows[r].Length && IsJunction(rows[r][end]))
                drawn.Add(Line(index, nodes[rows[r][end]], rows[r][(c + 1)..end], end - c, MetresAcross, horizontal: true));

            // Vertical: run down the column in the same way.
            var below = r + 1;
            while (below < rows.Length && IsLine(At(below, c))) below++;
            if (below > r + 1 && below < rows.Length && IsJunction(At(below, c)))
            {
                var run = new string(Enumerable.Range(r + 1, below - r - 1).Select(i => At(i, c)).ToArray());
                drawn.Add(Line(index, nodes[At(below, c)], run, below - r, MetresDown, horizontal: false));
            }
        }

        var groups = Enumerable.Range(0, drawn.Count).ToArray();
        var segmentsByEnds = new Dictionary<(int, int), int>();
        for (var i = 0; i < drawn.Count; i++) segmentsByEnds[Key(drawn[i].From, drawn[i].To)] = i;

        var names = nodes.ToDictionary(kv => kv.Value, kv => kv.Key);
        int SegmentIndex(string ends)
        {
            var (a, b) = ParseEnds(ends);
            return segmentsByEnds.TryGetValue(Key(nodes[a], nodes[b]), out var s)
                ? s
                : throw new ArgumentException($"No Segment {ends} on the map.");
        }
        foreach (var group in sameGround)
        {
            var members = group.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(SegmentIndex).ToArray();
            foreach (var m in members) groups[m] = members[0];
        }

        var segments = drawn.Select((d, i) => new Segment(
            Key: new SegmentKey(OsmWayId: i + 1, FromNodeId: d.From + 1, ToNodeId: d.To + 1),
            From: d.From,
            To: d.To,
            LengthM: d.Length,
            Ground: d.Ground,
            Access: d.Forbidden ? AccessKind.Forbidden : AccessKind.Allowed,
            SameGroundGroup: groups[i],
            MainRoad: d.MainRoad) { Obstacles = d.Stile ? [new ObstacleShare(ObstacleKind.Stile, 1)] : null, Carriageway = d.Carriageway, Busy = d.Busy }).ToList();

        return new AsciiNetwork(new NetworkSlice(points, segments), nodes, segmentsByEnds);
    }

    private static bool IsJunction(char ch) => char.IsAsciiLetterUpper(ch) || char.IsAsciiDigit(ch);

    private static bool IsLine(char ch) => ch is '.' or ':' or '-' or '|' or '=' or '#' or 'x' or 'm' or 's' or 'c' or 'b';

    private static (int, int, double, GroundKind, bool, bool, bool, bool, bool) Line(int from, int to, string run, int cells, double cellMetres, bool horizontal)
    {
        var kinds = run.Where(ch => ch is not ('x' or 'm' or 's' or 'c' or 'b')).Distinct().ToArray();
        if (kinds.Length > 1)
            throw new ArgumentException($"A line mixes '{string.Join("', '", kinds)}': draw one kind per Segment.");
        var ground = kinds.Length == 0 ? GroundKind.TrafficFree : kinds[0] switch
        {
            '.' when horizontal => GroundKind.OffRoad,
            ':' when !horizontal => GroundKind.OffRoad,
            '-' when horizontal => GroundKind.TrafficFree,
            '|' when !horizontal => GroundKind.TrafficFree,
            '=' when horizontal => GroundKind.Connector,
            '#' when !horizontal => GroundKind.Connector,
            var other => throw new ArgumentException($"'{other}' can't be used on a {(horizontal ? "horizontal" : "vertical")} line."),
        };
        var busy = run.Contains('b', StringComparison.Ordinal);
        var carriageway = busy || run.Contains('c', StringComparison.Ordinal);
        var mainRoad = carriageway || run.Contains('m', StringComparison.Ordinal);
        if (mainRoad && ground != GroundKind.Connector) throw new ArgumentException("Only a road (= or #) can be a Main Road.");
        return (from, to, cells * cellMetres, ground, run.Contains('x', StringComparison.Ordinal), mainRoad, run.Contains('s', StringComparison.Ordinal), carriageway, busy);
    }

    private static (int, int) Locate(string[] rows, char name)
    {
        for (var r = 0; r < rows.Length; r++)
        {
            var c = rows[r].IndexOf(name, StringComparison.Ordinal);
            if (c >= 0) return (r, c);
        }
        throw new ArgumentException($"No junction '{name}'.");
    }

    private static (char, char) ParseEnds(string ends) =>
        ends.Length == 3 && ends[1] == '-' ? (ends[0], ends[2]) : throw new ArgumentException($"Write Segments as \"A-B\", not \"{ends}\".");

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
}
