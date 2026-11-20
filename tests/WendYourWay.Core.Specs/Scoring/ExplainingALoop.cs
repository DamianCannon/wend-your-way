using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Why a Loop goes the way it does, said along it on the map (T157, #161; user: "I actually like the
/// map labels in their own right"): its Crossings, its Obstacles, its stretches of road, and its
/// longest green stretch, each at its Traversal. Only what the Network knows, and never a different
/// count from the Breakdown's.
/// </summary>
public static class ExplainingALoop
{
    private static NetworkSlice With(AsciiNetwork network, Func<Segment, Segment> change, params string[] segments)
    {
        var slice = network.Slice;
        var changed = segments.Select(s => slice.Segments[network.Segment(s)]).ToHashSet();
        return new NetworkSlice([.. Enumerable.Range(0, slice.NodeCount).Select(slice.Node)], [.. slice.Segments.Select(s => changed.Contains(s) ? change(s) : s)]);
    }

    [TestFixture]
    public class A_Loop_on_mixed_ground
    {
        //  S..s..A: 300 m Off-road with a stile; A=====B#C: 500 m of quiet road; C-----D:S: 800 m green,
        //  which runs on round the start into S..A, 1.1 km in all.
        private static readonly AsciiNetwork Map = AsciiNetwork.Parse("""
            S..s..A=====B
            :           #
            D-----------C
            """);

        private static readonly Loop Loop = Map.LoopThrough("SABCDS");
        private static readonly IReadOnlyList<LoopNote> Notes = Explanation.Of(Map.Slice, Loop);

        [Test]
        public void Then_its_notes_are_in_the_order_it_goes() =>
            Assert.That(Notes.Select(n => (n.Kind, n.Traversal)), Is.EqualTo(new[] { (NoteKind.Obstacle, 0), (NoteKind.Road, 1), (NoteKind.Green, 3) }));

        [Test]
        public void Then_the_stile_is_noted_where_it_is() =>
            Assert.That(Notes[0], Is.EqualTo(new LoopNote(NoteKind.Obstacle, 0, NotePlace.Middle) { Obstacle = ObstacleKind.Stile }));

        //  Said halfway along: 250 m into its 500 m, on A=====B.
        [Test]
        public void Then_its_road_is_noted_with_its_length_halfway_along() =>
            Assert.That((Notes[1].LengthM, Notes[1].MainRoadM, Notes[1].Place), Is.EqualTo((500.0, 0.0, NotePlace.Middle)));

        //  Halfway along its 1.1 km is on C-----D.
        [Test]
        public void Then_its_longest_green_stretch_is_the_Breakdown_s() =>
            Assert.That(Notes[2].LengthM, Is.EqualTo(Breakdown.Of(Map.Slice, Loop).LongestGreenM).And.EqualTo(1100));
    }

    [TestFixture]
    public class A_stretch_of_road_short_then_long
    {
        //  A=B is 100 m, B=======C 400 m: halfway along the 500 m is on B======C, not where it starts.
        [Test]
        public void Then_it_s_noted_halfway_along() =>
            Assert.That(Explanation.Of(Map.Slice, Map.LoopThrough("SABCDES")).Single(n => n.Kind == NoteKind.Road).Traversal, Is.EqualTo(2));

        //  250 m along, 150 m into B=======C's 400 m: so the map says it halfway along the road, not of the Segment.
        [Test]
        public void Then_how_far_along_its_Segment_is_said() =>
            Assert.That(Explanation.Of(Map.Slice, Map.LoopThrough("SABCDES")).Single(n => n.Kind == NoteKind.Road).Along, Is.EqualTo(0.375).Within(1e-9));

        private static readonly AsciiNetwork Map = AsciiNetwork.Parse("""
            S.....A=B=======C
            :               :
            E...............D
            """);
    }

    [TestFixture]
    public class A_short_link_of_quiet_road
    {
        //  100 m of road between two green stretches: bridged, not worth a word.
        [Test]
        public void Then_it_isn_t_noted() =>
            Assert.That(Explanation.Of(Map.Slice, Map.LoopThrough("SABCDS")).Select(n => n.Kind), Has.No.Member(NoteKind.Road));

        private static readonly AsciiNetwork Map = AsciiNetwork.Parse("""
            S.....A=B
            :       :
            D.......C
            """);
    }

    [TestFixture]
    public class A_Main_Road
    {
        //  Shorter than a quiet road needs: a Main Road is used only for short links, and it's what the user
        //  looks for. Not a few metres of one, though (found trying it: "18 m of road, all beside a Main Road").
        [Test]
        public void Then_a_short_stretch_on_one_is_noted() =>
            Assert.That((Roads("A=mB"), Roads("A==B")), Is.EqualTo((1, 0)));

        //  150 m of road, A to B, between green.
        private static int Roads(string road)
        {
            var map = AsciiNetwork.Parse($"""
                S.....{road}.....C
                :              :
                E..............D
                """);
            return Explanation.Of(map.Slice, map.LoopThrough("SABCDES")).Count(n => n.Kind == NoteKind.Road);
        }

        [Test]
        public void Then_a_few_metres_of_one_aren_t() =>
            Assert.That(Explanation.Of(With(Few, s => s with { MainRoad = true }, "A-B"), Few.LoopThrough("SABCDES")).Select(n => n.Kind), Has.No.Member(NoteKind.Road));

        //  A=B made 100 m of Main Road, between green.
        private static readonly AsciiNetwork Few = AsciiNetwork.Parse("""
            S.....A=B.....C
            :             :
            E.............D
            """);

        //  A Main Road with no carriageway drawn is a pavement beside one.
        [Test]
        public void Then_on_its_pavement_it_says_so() =>
            Assert.That(RoadOn("S=m=A").Pavement, Is.True);

        [Test]
        public void Then_on_its_carriageway_it_doesn_t() =>
            Assert.That(RoadOn("S=c=A").Pavement, Is.False);

        private static LoopNote RoadOn(string top)
        {
            var map = AsciiNetwork.Parse($"""
                {top}
                :   :
                C...B
                """);
            return Explanation.Of(map.Slice, map.LoopThrough("SABCS")).Single(n => n.Kind == NoteKind.Road);
        }
    }

    [TestFixture]
    public class An_Obstacle_at_a_junction
    {
        //  Half a gate on each side of B: one gate, at B, where A..B ends.
        private static readonly AsciiNetwork Map = AsciiNetwork.Parse("""
            S.....A
            :     :
            C.....B
            """);

        private static readonly NetworkSlice Gated = With(Map, s => s with { Obstacles = [new ObstacleShare(ObstacleKind.Gate, 0.5)] }, "A-B", "B-C");
        private static readonly Loop Loop = Map.LoopThrough("SABCS");

        [Test]
        public void Then_it_s_noted_once_at_the_junction() =>
            Assert.That(Explanation.Of(Gated, Loop).Where(n => n.Kind == NoteKind.Obstacle).Select(n => (n.Traversal, n.Place, n.Obstacle)),
                Is.EqualTo(new[] { (1, NotePlace.End, (ObstacleKind?)ObstacleKind.Gate) }));

        [Test]
        public void Then_the_Breakdown_counts_as_many() =>
            Assert.That(Explanation.Of(Gated, Loop).Count(n => n.Kind == NoteKind.Obstacle), Is.EqualTo(Breakdown.Of(Gated, Loop).Obstacles!.Sum(o => o.Count)));
    }

    [TestFixture]
    public class A_crossing_way
    {
        //  A zebra over a Main Road, mapped as a crossing way of two Segments, A-B and B-C: one Crossing.
        private static readonly AsciiNetwork Map = AsciiNetwork.Parse("""
            S.....A
            :     |
            :     B
            :     |
            E.....C
            """);

        private static readonly NetworkSlice Crossed = With(Map, s => s with { Key = s.Key with { OsmWayId = 99 }, Crossing = CrossingKind.Marked, CrossesMainRoad = true, CrossingShare = 0.5 }, "A-B", "B-C");
        private static readonly Loop Loop = Map.LoopThrough("SABCES");

        [Test]
        public void Then_it_s_noted_once_by_its_kind() =>
            Assert.That(Explanation.Of(Crossed, Loop).Where(n => n.Kind == NoteKind.Crossing).Select(n => (n.Crossing, n.MainRoad)),
                Is.EqualTo(new[] { ((CrossingKind?)CrossingKind.Marked, true) }));

        //  Where its two Segments meet, mid-road.
        [Test]
        public void Then_it_s_noted_in_the_middle_of_the_road() =>
            Assert.That(Explanation.Of(Crossed, Loop).Single(n => n.Kind == NoteKind.Crossing) is { Traversal: 1, Place: NotePlace.End });

        [Test]
        public void Then_the_Breakdown_counts_as_many() =>
            Assert.That(Explanation.Of(Crossed, Loop).Count(n => n.Kind == NoteKind.Crossing), Is.EqualTo(Breakdown.Of(Crossed, Loop).Crossings));
    }

    [TestFixture]
    public class A_Main_Road_crossed_at_a_junction
    {
        //  Straight over a Main Road's carriageway at J, with no crossing way: a crossing link (T145).
        private const string Across = """
                  S
                  #
            W==c==J==c==E
                  #
                  T
            """;

        [Test]
        public void Then_it_s_noted_as_a_Crossing_with_nothing_marked_there()
        {
            var drawn = AsciiNetwork.Parse(Across);
            var slice = Corners.Split(drawn.Slice);
            int At(char name) => Enumerable.Range(0, slice.NodeCount).First(n => slice.Node(n) == drawn.Slice.Node(drawn.Node(name)));
            var path = PathFinder.Find(slice, PathEnd.AtNode(At('S')), PathEnd.AtNode(At('T')), new RunCostModel(ScoringWeights.Default))!;
            var loop = new Loop(At('S'), [], path.Traversals);
            var crossings = Explanation.Of(slice, loop).Where(n => n.Kind == NoteKind.Crossing).ToArray();
            Assert.That((crossings.Select(n => n.Crossing), crossings.Length), Is.EqualTo((new CrossingKind?[] { CrossingKind.Unmarked }, Breakdown.Of(slice, loop).Crossings)));
        }
    }
}
