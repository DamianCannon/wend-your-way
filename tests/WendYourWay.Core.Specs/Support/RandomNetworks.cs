using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;

namespace WendYourWay.Core.Specs.Support;

/// <summary>
/// Seeded random networks and cost models, for checking the path finder against a reference
/// search. Every network is reproducible from its seed.
/// </summary>
internal static class RandomNetworks
{
    /// <summary>
    /// A connected-ish network of <paramref name="nodes"/> junctions scattered over a few
    /// kilometres. Each junction joins its nearest neighbours; Segment lengths are the straight
    /// line plus up to 50% (real ways bend). Ground is mixed, about 10% of Segments are
    /// forbidden, and some Segments share same-ground groups.
    /// </summary>
    public static NetworkSlice Create(int seed, int nodes)
    {
        var random = new Random(seed);
        var points = Enumerable.Range(0, nodes)
            .Select(_ => new NodePoint(random.NextDouble() * 3000, random.NextDouble() * 3000))
            .ToArray();

        var pairs = new HashSet<(int, int)>();
        for (var a = 0; a < nodes; a++)
        {
            var neighbours = Enumerable.Range(0, nodes)
                .Where(b => b != a)
                .OrderBy(b => points[a].DistanceTo(points[b]))
                .Take(random.Next(1, 5));
            foreach (var b in neighbours) pairs.Add(a < b ? (a, b) : (b, a));
        }

        var segments = new List<Segment>();
        foreach (var (a, b) in pairs.OrderBy(p => p))
        {
            var i = segments.Count;
            segments.Add(new Segment(
                Key: new SegmentKey(i + 1, a + 1, b + 1),
                From: a,
                To: b,
                LengthM: points[a].DistanceTo(points[b]) * (1 + (random.NextDouble() * 0.5)),
                Ground: (GroundKind)random.Next(3),
                Access: random.NextDouble() < 0.1 ? AccessKind.Forbidden : AccessKind.Allowed,
                SameGroundGroup: random.NextDouble() < 0.2 && i > 0 ? random.Next(i) : i));
        }

        return new NetworkSlice(points, segments);
    }

    /// <summary>
    /// A cost model with arbitrary per-Segment multipliers between <paramref name="floor"/> and 6,
    /// including discounts below the base weights (as Gem Marks and Novelty will give), plus
    /// some impassable Segments. Its floor is honest.
    /// </summary>
    public static ICostModel CostModel(int seed, NetworkSlice slice, double floor) =>
        new ArbitraryCostModel(seed, slice, floor);

    private sealed class ArbitraryCostModel : ICostModel
    {
        private readonly Dictionary<SegmentKey, double> _perMetre = [];

        public ArbitraryCostModel(int seed, NetworkSlice slice, double floor)
        {
            var random = new Random(seed ^ 0x5eed);
            FloorPerMetre = floor;
            foreach (var s in slice.Segments)
            {
                _perMetre[s.Key] = s.Access == AccessKind.Forbidden
                    ? double.PositiveInfinity
                    : floor + (random.NextDouble() * (6 - floor));
            }
        }

        public double FloorPerMetre { get; }

        public double CostOf(Segment segment, bool forward) => _perMetre[segment.Key] * segment.LengthM;
    }
}
