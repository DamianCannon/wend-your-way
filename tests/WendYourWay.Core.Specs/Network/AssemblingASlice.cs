using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Network;

/// <summary>
/// A slice is assembled from Segments as the database stores them, by OSM identity. However the
/// rows arrive, the slice and every Path through it come out the same.
/// </summary>
public static class AssemblingASlice
{
    [TestFixture]
    public class A_grid_full_of_equally_cheap_Paths
    {
        //  Every way from S to T along the grid is 800 m Off-road. Off-road costs exactly the
        //  cost model's floor, so A*'s estimates tie exactly too, and junction numbering decides
        //  which of the equal Paths it returns.
        private const string Map = """
            S.....A.....B
            :     :     :
            C.....D.....E
            :     :     :
            F.....G.....T
            """;

        private AsciiNetwork _drawn = null!;
        private List<StoredSegment> _stored = null!;

        [OneTimeSetUp]
        public void Arrange()
        {
            _drawn = AsciiNetwork.Parse(Map);
            _stored = [.. _drawn.Slice.Segments.Select(s => new StoredSegment(
                s.Key, _drawn.Slice.Node(s.From), _drawn.Slice.Node(s.To), s.LengthM, s.Ground, s.Access))];
        }

        private List<(SegmentKey, bool)> PathFromSToT(NetworkSlice slice)
        {
            int At(char junction) =>
                Enumerable.Range(0, slice.NodeCount).Single(n => slice.Node(n) == _drawn.Slice.Node(_drawn.Node(junction)));
            var path = PathFinder.Find(slice, PathEnd.AtNode(At('S')), PathEnd.AtNode(At('T')), new RunCostModel(ScoringWeights.Default));
            return [.. path!.Traversals.Select(t => (slice.Segments[t.Segment].Key, t.Forward))];
        }

        [Test]
        public void Then_it_routes_as_the_drawn_network() =>
            Assert.That(PathFromSToT(NetworkSlice.Assemble(_stored)), Is.EqualTo(PathFromSToT(_drawn.Slice)));

        [Test]
        public void Then_the_order_rows_arrive_in_never_changes_the_Path()
        {
            var expected = PathFromSToT(NetworkSlice.Assemble(_stored));
            var random = new Random(29);
            for (var shuffle = 0; shuffle < 50; shuffle++)
            {
                var rows = _stored.ToArray();
                random.Shuffle(rows);
                Assert.That(PathFromSToT(NetworkSlice.Assemble(rows)), Is.EqualTo(expected), $"shuffle {shuffle}");
            }
        }
    }

    [TestFixture]
    public class Segments_stored_beside_a_road
    {
        //  A 100 m road (way 1), a pavement beside all of it (way 3), a 150 m path beside it for
        //  100 m before it bends away (way 5), and a path nowhere near it (way 7).
        private static readonly SegmentKey Road = new(1, 1, 2);

        private static StoredSegment Way(long id, double lengthM, params RoadShare[] beside) => new(
            new SegmentKey(id, id, id + 1), new NodePoint(0, id * 1000), new NodePoint(0, (id * 1000) + lengthM), lengthM,
            GroundKind.Connector, AccessKind.Allowed, beside);

        private NetworkSlice _slice = null!;

        [OneTimeSetUp]
        public void Arrange() => _slice = NetworkSlice.Assemble(
            [Way(1, 100), Way(3, 100, new RoadShare(Road, 100)), Way(5, 150, new RoadShare(Road, 100)), Way(7, 100)]);

        private IReadOnlyList<GroundShare> GroundOf(long wayId) => _slice.Segments.Single(s => s.Key.OsmWayId == wayId).SameGround;

        [Test]
        public void Then_a_pavement_lies_wholly_on_its_roads_ground() =>
            Assert.That(GroundOf(3), Is.EqualTo(GroundOf(1)));

        [Test]
        public void Then_a_path_lies_on_its_roads_ground_only_as_far_as_it_runs_beside_it() =>
            Assert.That(GroundOf(5).Select(g => (g.Group == GroundOf(1).Single().Group, g.Metres)),
                Is.EquivalentTo(new[] { (true, 100.0), (false, 50.0) }));

        [Test]
        public void Then_every_other_Segment_is_ground_of_its_own() =>
            Assert.That(new[] { GroundOf(1).Single().Group, GroundOf(5)[^1].Group, GroundOf(7).Single().Group }, Is.Unique);
    }
}
