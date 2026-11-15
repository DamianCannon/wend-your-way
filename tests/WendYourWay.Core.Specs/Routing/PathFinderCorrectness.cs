using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Routing;

public static class PathFinderCorrectness
{
    /// <summary>
    /// The path finder must find exactly the cheapest Path that a slow, obviously correct search
    /// finds, on hundreds of random networks with mixed ground, forbidden Segments, shared
    /// same-ground groups and costs both above and below the base weights.
    /// </summary>
    [TestFixture]
    public class It_agrees_with_a_naive_reference_search
    {
        private static IEnumerable<int> Seeds() => Enumerable.Range(1, 300);

        [TestCaseSource(nameof(Seeds))]
        public void Then_the_cheapest_cost_matches(int seed)
        {
            var slice = RandomNetworks.Create(seed, nodes: 10 + (seed % 50));
            var cost = RandomNetworks.CostModel(seed, slice, floor: 0.2 + (seed % 7 * 0.15));
            var from = seed % slice.NodeCount;
            var to = (seed * 7 + 3) % slice.NodeCount;

            var expected = ReferenceSearch.CheapestCost(slice, from, to, cost);
            var actual = PathFinder.Find(slice, PathEnd.AtNode(from), PathEnd.AtNode(to), cost)?.Cost;

            if (expected is null) Assert.That(actual, Is.Null);
            else Assert.That(actual, Is.EqualTo(expected.Value).Within(1e-9).Percent);
        }

        //  One search reused for several Paths, as a Loop uses it: nothing from one Path may
        //  leak into the next.
        [TestCaseSource(nameof(Seeds))]
        public void Then_a_search_reused_for_several_Paths_matches_each_time(int seed)
        {
            var slice = RandomNetworks.Create(seed, nodes: 10 + (seed % 50));
            var cost = RandomNetworks.CostModel(seed, slice, floor: 0.2 + (seed % 7 * 0.15));
            var search = new PathSearch(slice);
            for (var i = 0; i < 5; i++)
            {
                var from = (seed + (i * 13)) % slice.NodeCount;
                var to = ((seed * 7) + (i * 5) + 3) % slice.NodeCount;
                var expected = ReferenceSearch.CheapestCost(slice, from, to, cost);
                var actual = search.Find(PathEnd.AtNode(from), PathEnd.AtNode(to), cost)?.Cost;

                if (expected is null) Assert.That(actual, Is.Null, $"Path {i}");
                else Assert.That(actual, Is.EqualTo(expected.Value).Within(1e-9).Percent, $"Path {i}");
            }
        }

        [TestCaseSource(nameof(Seeds))]
        public void Then_the_Run_cost_model_matches_too(int seed)
        {
            var slice = RandomNetworks.Create(seed, nodes: 10 + (seed % 50));
            var used = slice.Segments.Where((_, i) => i % 5 == seed % 5).Select(s => s.SameGround.Single().Group).ToHashSet();
            var cost = new RunCostModel(ScoringWeights.Default, usedGround: used);
            var from = (seed * 3) % slice.NodeCount;
            var to = (seed * 11 + 1) % slice.NodeCount;

            var expected = ReferenceSearch.CheapestCost(slice, from, to, cost);
            var actual = PathFinder.Find(slice, PathEnd.AtNode(from), PathEnd.AtNode(to), cost)?.Cost;

            if (expected is null) Assert.That(actual, Is.Null);
            else Assert.That(actual, Is.EqualTo(expected.Value).Within(1e-9).Percent);
        }
    }

    [TestFixture]
    public class A_cost_model_that_undercuts_its_own_floor
    {
        //  A* is only correct if no cost falls below FloorPerMetre × length. A model that
        //  claims a floor of 1 but charges 0.5 per metre would silently give wrong Paths.
        private sealed class Dishonest : ICostModel
        {
            public double FloorPerMetre => 1.0;

            public double CostOf(Segment segment, bool forward) => 0.5 * segment.LengthM;
        }

        [Test]
        public void Then_the_path_finder_refuses_rather_than_guess()
        {
            var map = AsciiNetwork.Parse("""
                S-----A-----T
                """);
            Assert.That(
                () => PathFinder.Find(map.Slice, PathEnd.AtNode(map.Node('S')), PathEnd.AtNode(map.Node('T')), new Dishonest()),
                Throws.InvalidOperationException.With.Message.Contains("floor"));
        }
    }

    [TestFixture]
    public class When_several_Paths_cost_exactly_the_same
    {
        //  Square 200 m Traffic-free blocks (four characters across, two lines down), so
        //  there are many exactly equal ways from S to T, and A* sees exact ties between them.
        private const string Map = """
            S---A---B---C
            |   |   |   |
            D---E---F---G
            |   |   |   |
            H---I---J---K
            |   |   |   |
            L---M---N---T
            """;

        [Test]
        public void Then_the_same_one_is_chosen_whatever_order_the_Segments_arrive_in()
        {
            var map = AsciiNetwork.Parse(Map);
            var original = KeysOfPath(map.Slice, map.Node('S'), map.Node('T'));

            foreach (var seed in Enumerable.Range(1, 20))
            {
                var shuffled = Shuffle(map.Slice, seed);
                Assert.That(KeysOfPath(shuffled, map.Node('S'), map.Node('T')), Is.EqualTo(original), $"shuffle {seed}");
            }
        }

        private static SegmentKey[] KeysOfPath(NetworkSlice slice, int from, int to) =>
            PathFinder.Find(slice, PathEnd.AtNode(from), PathEnd.AtNode(to), new RunCostModel(ScoringWeights.Default))!
                .Traversals.Select(t => slice.Segments[t.Segment].Key).ToArray();

        private static NetworkSlice Shuffle(NetworkSlice slice, int seed)
        {
            var nodes = Enumerable.Range(0, slice.NodeCount).Select(slice.Node).ToArray();
            var segments = slice.Segments.ToArray();
            new Random(seed).Shuffle(segments);
            return new NetworkSlice(nodes, segments);
        }
    }

    [TestFixture]
    public class A_Segment_shorter_than_the_distance_between_its_ends
    {
        //  A* assumes no Segment is shorter than the straight line between its junctions.
        //  Real geometry can't be, so such a Segment means corrupt data and is refused.
        [Test]
        public void Then_the_slice_refuses_it()
        {
            var nodes = new[] { new NodePoint(0, 0), new NodePoint(300, 400) };   // 500 m apart
            var tooShort = new Segment(new SegmentKey(1, 1, 2), 0, 1, LengthM: 450,
                GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0);

            Assert.That(() => new NetworkSlice(nodes, [tooShort]), Throws.ArgumentException.With.Message.Contains("shorter"));
        }
    }
}
