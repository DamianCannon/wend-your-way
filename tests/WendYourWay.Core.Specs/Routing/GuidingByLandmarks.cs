using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Routing;

/// <summary>
/// Landmarks (PLAN T068): the path finder guided by the cheapest costs to and from a few junctions
/// round the edge of the slice, not only by the straight line, finds Paths as cheap as ever, and
/// looks at far fewer junctions on the way across a 100 km slice.
/// </summary>
public static class GuidingByLandmarks
{
    [TestFixture]
    public class It_agrees_with_a_naive_reference_search
    {
        private static IEnumerable<int> Seeds() => Enumerable.Range(1, 300);

        //  Each way costing differently, as a one-way cost would: up to 4 times as much backwards.
        [TestCaseSource(nameof(Seeds))]
        public void Then_the_cheapest_cost_matches(int seed)
        {
            var slice = RandomNetworks.Create(seed, nodes: 10 + (seed % 50));
            var cost = new EachWay(RandomNetworks.CostModel(seed, slice, floor: 0.2 + (seed % 7 * 0.15)), seed);
            var landmarks = Landmarks.Place(slice, [.. Enumerable.Range(0, slice.NodeCount)], 1 + (seed % 8), cost);
            var from = seed % slice.NodeCount;
            var to = (seed * 7 + 3) % slice.NodeCount;

            var expected = ReferenceSearch.CheapestCost(slice, from, to, cost);
            var actual = new PathSearch(slice).Find(PathEnd.AtNode(from), PathEnd.AtNode(to), cost, landmarks)?.Cost;

            if (expected is null) Assert.That(actual, Is.Null);
            else Assert.That(actual, Is.EqualTo(expected.Value).Within(1e-9).Percent);
        }

        //  As a Loop uses them: placed once for the Request, under the cheapest each Segment can
        //  cost, and guiding Paths with ground already used dearer. Climbing makes each way cost
        //  differently, and with the Hills Lean at its strongest, ground retraced can cost less
        //  than fresh (4 times flat against up to 5 times for a hilly Lean).
        [TestCaseSource(nameof(Seeds))]
        public void Then_the_Run_cost_model_matches_with_ground_used_and_climbing(int seed)
        {
            var slice = Climbing(RandomNetworks.Create(seed, nodes: 10 + (seed % 50)), seed);
            var weights = ScoringWeights.Default with { HillsLean = (seed % 3) - 1, FewerHillsCostWeight = 5, MoreHillsCostWeight = 4 };
            var landmarks = Landmarks.Place(slice, [.. Enumerable.Range(0, slice.NodeCount)], 1 + (seed % 8), RunCostModel.Cheapest(weights));
            var used = slice.Segments.Where((_, i) => i % 3 == seed % 3).Select(s => s.SameGround.Single().Group).ToHashSet();
            var cost = new RunCostModel(weights, usedGround: used);
            var search = new PathSearch(slice);
            for (var i = 0; i < 5; i++)
            {
                var from = (seed + (i * 13)) % slice.NodeCount;
                var to = ((seed * 7) + (i * 5) + 3) % slice.NodeCount;
                var expected = ReferenceSearch.CheapestCost(slice, from, to, cost);
                var actual = search.Find(PathEnd.AtNode(from), PathEnd.AtNode(to), cost, landmarks)?.Cost;

                if (expected is null) Assert.That(actual, Is.Null, $"Path {i}");
                else Assert.That(actual, Is.EqualTo(expected.Value).Within(1e-9).Percent, $"Path {i}");
            }
        }

        //  The bound itself, from every junction to one end: never above the cheapest cost.
        [TestCaseSource(nameof(Seeds))]
        public void Then_the_bound_is_never_above_the_cheapest_cost_either_way(int seed)
        {
            var slice = RandomNetworks.Create(seed, nodes: 10 + (seed % 50));
            var cost = new EachWay(RandomNetworks.CostModel(seed, slice, floor: 0.2 + (seed % 7 * 0.15)), seed);
            var landmarks = Landmarks.Place(slice, [.. Enumerable.Range(0, slice.NodeCount)], 1 + (seed % 8), cost);
            var to = (seed * 7 + 3) % slice.NodeCount;
            var over = Enumerable.Range(0, slice.NodeCount)
                .Where(n => ReferenceSearch.CheapestCost(slice, n, to, cost) is { } cheapest && landmarks.Bound(n, to) > cheapest * (1 + 1e-9))
                .ToArray();
            Assert.That(over, Is.Empty);
        }

        [TestCaseSource(nameof(Seeds))]
        public void Then_the_bound_is_never_above_the_cheapest_cost(int seed)
        {
            var slice = Climbing(RandomNetworks.Create(seed, nodes: 10 + (seed % 50)), seed);
            var weights = ScoringWeights.Default with { HillsLean = (seed % 3) - 1, FewerHillsCostWeight = 5, MoreHillsCostWeight = 4 };
            var landmarks = Landmarks.Place(slice, [.. Enumerable.Range(0, slice.NodeCount)], 1 + (seed % 8), RunCostModel.Cheapest(weights));
            var used = slice.Segments.Where((_, i) => i % 3 == seed % 3).Select(s => s.SameGround.Single().Group).ToHashSet();
            var cost = new RunCostModel(weights, usedGround: used);
            var to = (seed * 7 + 3) % slice.NodeCount;
            var over = Enumerable.Range(0, slice.NodeCount)
                .Where(n => ReferenceSearch.CheapestCost(slice, n, to, cost) is { } cheapest && landmarks.Bound(n, to) > cheapest * (1 + 1e-9))
                .ToArray();
            Assert.That(over, Is.Empty);
        }

        //  The map moved on: a Request cancelled while its Landmarks are placed stops, rather
        //  than finish 16 searches across every processor (T068 review).
        [Test]
        public void Then_placing_them_stops_when_the_Request_is_cancelled()
        {
            var slice = RandomNetworks.Create(1, nodes: 50);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(
                () => Landmarks.Place(slice, [.. Enumerable.Range(0, slice.NodeCount)], 8, RandomNetworks.CostModel(1, slice, floor: 0.5), cancelled.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }

        // Backwards, each Segment costs 1 to 4 times what it costs forwards.
        private sealed class EachWay(ICostModel forwards, int seed) : ICostModel
        {
            private readonly Random _random = new(seed ^ 0x0e1e);
            private readonly Dictionary<SegmentKey, double> _backwards = [];

            public double FloorPerMetre => forwards.FloorPerMetre;

            public double CostOf(Segment segment, bool forward)
            {
                var cost = forwards.CostOf(segment, forward);
                if (forward) return cost;
                lock (_backwards)
                {
                    if (!_backwards.TryGetValue(segment.Key, out var times)) _backwards[segment.Key] = times = 1 + (_random.NextDouble() * 3);
                    return cost * times;
                }
            }
        }

        // Flat, or up to 1 in 10 up one way and less down the other.
        private static NetworkSlice Climbing(NetworkSlice slice, int seed)
        {
            var random = new Random(seed ^ 0xc11b);
            return new NetworkSlice(
                [.. Enumerable.Range(0, slice.NodeCount).Select(slice.Node)],
                [.. slice.Segments.Select(s => random.NextDouble() < 0.4 ? s
                    : s with { AscentM = random.NextDouble() * 0.1 * s.LengthM, DescentM = random.NextDouble() * 0.05 * s.LengthM })]);
        }
    }
}
