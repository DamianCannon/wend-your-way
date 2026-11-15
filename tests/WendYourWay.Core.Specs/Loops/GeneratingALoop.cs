using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

public static class GeneratingALoop
{
    /// <summary>Walks a Loop's Traversals from its Start, returning the junctions it passes in order.</summary>
    internal static List<int> JunctionsAlong(NetworkSlice slice, Loop loop)
    {
        var at = loop.Start;
        var passed = new List<int> { at };
        foreach (var traversal in loop.Traversals)
        {
            var segment = slice.Segments[traversal.Segment];
            Assert.That(traversal.Forward ? segment.From : segment.To, Is.EqualTo(at), $"{segment.Key} doesn't start where the last Segment ended");
            at = traversal.Forward ? segment.To : segment.From;
            passed.Add(at);
        }
        return passed;
    }

    [TestFixture]
    public class On_a_grid_of_paths
    {
        //  2 km square, a junction every 100 m; a 4 km Run from near the middle.
        private NetworkSlice _grid = null!;
        private Loop? _loop;

        [OneTimeSetUp]
        public void Arrange()
        {
            _grid = GridNetwork.Create(junctionsAcross: 21);
            _loop = LoopGenerator.Generate(_grid, new Request(new NodePoint(1030, 970), DistanceKm: 4), ScoringWeights.Default);
        }

        [Test]
        public void Then_a_Loop_leaves_its_start_and_comes_back_to_it()
        {
            Assert.That(_loop, Is.Not.Null);
            var junctions = JunctionsAlong(_grid, _loop!);
            Assert.That(junctions[^1], Is.EqualTo(_loop!.Start));
            Assert.That(junctions.Distinct().Count(), Is.GreaterThan(2));
        }
    }

    [TestFixture]
    public class On_a_grid_of_winding_paths
    {
        //  Every path is 1.6 times the straight line, so a circle sized for streets would make
        //  Loops far too long: the generator has to measure and correct.
        private NetworkSlice _grid = null!;
        private Loop? _loop;

        [OneTimeSetUp]
        public void Arrange()
        {
            _grid = GridNetwork.Create(junctionsAcross: 21, winding: 1.6);
            _loop = LoopGenerator.Generate(_grid, new Request(new NodePoint(1030, 970), DistanceKm: 4), ScoringWeights.Default);
        }

        [Test]
        public void Then_the_Loop_is_within_1_km_of_the_Distance() =>
            Assert.That(Breakdown.Of(_grid, _loop!).LengthM, Is.InRange(3000, 5000));
    }

    [TestFixture]
    public class Next_to_Off_road_ground
    {
        //  The western half of the grid is Off-road, the eastern half Traffic-free, and the start
        //  is just east of the line between them. The best Loop goes west.
        private NetworkSlice _grid = null!;
        private Loop? _loop;

        [OneTimeSetUp]
        public void Arrange()
        {
            _grid = GridNetwork.Create(junctionsAcross: 21, ground: m => m.X < 1000 ? GroundKind.OffRoad : GroundKind.TrafficFree);
            _loop = LoopGenerator.Generate(_grid, new Request(new NodePoint(1030, 970), DistanceKm: 4), ScoringWeights.Default);
        }

        [Test]
        public void Then_the_Loop_heads_for_the_Off_road_ground() =>
            Assert.That(Breakdown.Of(_grid, _loop!).OffRoadPercent, Is.GreaterThan(80));
    }

    //  The western half of the grid is hilly, each path climbing 3 m and dropping 3 m, the eastern
    //  half flat, and the start on the line between them (T050).
    [TestFixture(-1.0, false, TestName = "Leaning_to_fewer_hills")]
    [TestFixture(1.0, true, TestName = "Leaning_to_more_hills")]
    public class Next_to_hills(double lean, bool west)
    {
        private NetworkSlice _grid = null!;
        private Loop? _loop;

        [OneTimeSetUp]
        public void Arrange()
        {
            _grid = GridNetwork.Create(junctionsAcross: 21, climbing: m => m.X < 1000 ? 3 : 0);
            _loop = LoopGenerator.Generate(_grid, new Request(new NodePoint(1000, 970), DistanceKm: 4, HillsLean: lean), ScoringWeights.Default);
        }

        [Test]
        public void Then_the_Loop_heads_for_the_ground_it_leans_to()
        {
            var junctions = JunctionsAlong(_grid, _loop!);
            var westward = junctions.Count(n => _grid.Node(n).X < 1000) > junctions.Count / 2;
            Assert.That(westward, Is.EqualTo(west));
        }
    }

    //  The western half of the grid is bare Off-road, the eastern half Traffic-free wholly beside green
    //  areas, the start on the line between them (T058b). At the centre the Traffic-free is cheaper
    //  (1.1 × 0.7 = 0.77 a metre against 1.0); leaning fully to trails the Off-road is (0.5); leaning to
    //  tarmac it's dearer still.
    [TestFixture(1.0, true, TestName = "Leaning_to_trails")]
    [TestFixture(0.0, false, TestName = "Leaning_neither_way_on_trails")]
    [TestFixture(-1.0, false, TestName = "Leaning_to_tarmac")]
    public class Next_to_trails(double lean, bool west)
    {
        private NetworkSlice _grid = null!;
        private Loop? _loop;

        [OneTimeSetUp]
        public void Arrange()
        {
            _grid = GridNetwork.Create(junctionsAcross: 21, ground: m => m.X < 1000 ? GroundKind.OffRoad : GroundKind.TrafficFree,
                greenery: m => m.X < 1000 ? 0 : 1);
            _loop = LoopGenerator.Generate(_grid, new Request(new NodePoint(1000, 970), DistanceKm: 4, OffRoadLean: lean), ScoringWeights.Default);
        }

        [Test]
        public void Then_the_Loop_heads_for_the_ground_it_leans_to()
        {
            var junctions = JunctionsAlong(_grid, _loop!);
            var westward = junctions.Count(n => _grid.Node(n).X < 1000) > junctions.Count / 2;
            Assert.That(westward, Is.EqualTo(west));
        }
    }

    [TestFixture]
    public class Beside_an_island_of_paths
    {
        //  A little square of paths (an estate's, say) touches nothing else. The start point is
        //  nearer to it than to the grid, but no Loop can be made from it, so the start snaps
        //  to the grid, the piece of the Network that most of the slice is in.
        private NetworkSlice _grid = null!;
        private Loop? _loop;

        private static IEnumerable<StoredSegment> Island()
        {
            NodePoint[] corners = [new(2100, 1000), new(2140, 1000), new(2140, 1040), new(2100, 1040)];
            for (var i = 0; i < 4; i++)
                yield return new StoredSegment(new SegmentKey(9000 + i, 9000 + i, 9000 + ((i + 1) % 4)), corners[i], corners[(i + 1) % 4], 40, GroundKind.TrafficFree, AccessKind.Allowed);
        }

        [OneTimeSetUp]
        public void Arrange()
        {
            _grid = GridNetwork.Create(junctionsAcross: 21, extra: Island());
            _loop = LoopGenerator.Generate(_grid, new Request(new NodePoint(2090, 1010), DistanceKm: 4), ScoringWeights.Default);
        }

        [Test]
        public void Then_the_Loop_starts_on_the_grid() =>
            Assert.That(_grid.Node(_loop!.Start), Is.EqualTo(new NodePoint(2000, 1000)));
    }

    [TestFixture]
    public class When_no_Loop_can_be_made
    {
        //  The whole Network is a 400 m square of paths: nothing near 10 km fits (R1.5).
        [Test]
        public void Then_a_Network_too_small_for_the_Distance_gives_none() =>
            Assert.That(
                LoopGenerator.Generate(GridNetwork.Create(junctionsAcross: 5), new Request(new NodePoint(200, 200), DistanceKm: 10), ScoringWeights.Default),
                Is.Null);

        //  The grid's east edge is at 2,000 m; the start is 600 m beyond it, across ground with
        //  no ways. Snapping that far could put the start across a river (R1.2a).
        [Test]
        public void Then_a_start_more_than_500_m_from_any_way_gives_none() =>
            Assert.That(
                LoopGenerator.Generate(GridNetwork.Create(junctionsAcross: 21), new Request(new NodePoint(2600, 1000), DistanceKm: 4), ScoringWeights.Default),
                Is.Null);

        [Test]
        public void Then_a_cancelled_Request_stops()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(
                () => LoopGenerator.Generate(GridNetwork.Create(junctionsAcross: 21), new Request(new NodePoint(1030, 970), DistanceKm: 4), ScoringWeights.Default, cancelled.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void Then_an_empty_slice_gives_none() =>
            Assert.That(
                LoopGenerator.Generate(NetworkSlice.Assemble([]), new Request(new NodePoint(0, 0), DistanceKm: 5), ScoringWeights.Default),
                Is.Null);
    }

    [TestFixture]
    public class Among_dead_ends_and_forbidden_ways
    {
        //  An Off-road dead-end stub leaves every junction, and a scattering of ways is private.
        //  With Traffic-free ground made as dear as road, going out and back along a stub
        //  ((1 + 4) / 2 per metre) is cheaper than the grid (3 per metre), so Anchors at dead
        //  ends would score better. They still mustn't be used: a Spur must reach an Attraction
        //  (R2.6), and a dead end isn't one.
        private static readonly ScoringWeights StubsAreCheap = ScoringWeights.Default with { TrafficFreePerMetre = 3 };
        private NetworkSlice _grid = null!;
        private Loop? _loop;

        private static IEnumerable<StoredSegment> Stubs()
        {
            for (var row = 0; row < 21; row++)
            {
                for (var column = 0; column < 21; column++)
                {
                    var junction = new NodePoint(column * 100, row * 100);
                    var deadEnd = new NodePoint(junction.X + 30, junction.Y + 40);
                    yield return new StoredSegment(
                        new SegmentKey(5000 + (row * 21) + column, (row * 21) + column + 1, 5000 + (row * 21) + column),
                        junction, deadEnd, 50, GroundKind.OffRoad, AccessKind.Allowed);
                }
            }
        }

        [OneTimeSetUp]
        public void Arrange()
        {
            _grid = GridNetwork.Create(
                junctionsAcross: 21,
                forbidden: m => ((int)(m.X / 100) + (int)(m.Y / 100)) % 4 == 0,
                extra: Stubs());
            _loop = LoopGenerator.Generate(_grid, new Request(new NodePoint(1030, 970), DistanceKm: 4), StubsAreCheap);
        }

        [Test]
        public void Then_no_Segment_of_the_Loop_is_forbidden() =>
            Assert.That(_loop!.Traversals.Select(t => _grid.Segments[t.Segment].Access), Has.None.EqualTo(AccessKind.Forbidden));

        [Test]
        public void Then_every_Anchor_is_on_a_through_route()
        {
            int UsableSegmentsAt(int junction) =>
                _grid.SegmentsAt(junction).ToArray().Count(s => _grid.Segments[s].Access != AccessKind.Forbidden);
            Assert.That(_loop!.Anchors.Select(UsableSegmentsAt), Has.All.GreaterThanOrEqualTo(3));
        }
    }

    [TestFixture]
    public class From_a_start_reached_by_a_path_and_a_road
    {
        //  The start S is 1 km south of a grid, joined to it by an Off-road path and, beside it,
        //  a road through W. Every Loop must leave and return that way. Each Path is found with
        //  the ground earlier Paths used made dearer (4×, dearer than road), so the Loop goes out
        //  along the path and comes home by the road instead of Retracing it.
        private NetworkSlice _grid = null!;
        private Loop? _loop;

        private static IEnumerable<StoredSegment> TheWaysIn()
        {
            NodePoint s = new(500, -1000), w = new(400, -500), j = new(500, 0);
            const long S = 9001, W = 9002, J = 6;   // J is the grid's junction at (500, 0)
            yield return new StoredSegment(new SegmentKey(9001, S, J), s, j, 1000, GroundKind.OffRoad, AccessKind.Allowed);
            yield return new StoredSegment(new SegmentKey(9002, S, W), s, w, 510, GroundKind.Connector, AccessKind.Allowed);
            yield return new StoredSegment(new SegmentKey(9003, W, J), w, j, 510, GroundKind.Connector, AccessKind.Allowed);
        }

        [OneTimeSetUp]
        public void Arrange()
        {
            _grid = GridNetwork.Create(junctionsAcross: 11, extra: TheWaysIn());
            _loop = LoopGenerator.Generate(_grid, new Request(new NodePoint(500, -1000), DistanceKm: 4), ScoringWeights.Default);
        }

        [Test]
        public void Then_it_covers_no_ground_twice() =>
            Assert.That(_loop!.Traversals.Select(t => _grid.Segments[t.Segment].SameGround.Single().Group), Is.Unique);
    }
}
