using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Routing;

/// <summary>
/// Main Roads crossed at real crossings (#149, T145b). A route had come off a side road, run 66 m
/// along a four-lane trunk road and left by a side road on the other side: four lanes crossed with
/// no crossing, charged nothing (user, 2026-11-15: "a terrible place which I would NEVER choose").
/// Each junction on a Main Road's carriageway is split into its corners, one each side; going from
/// one to another crosses the road, and costs what that crossing is. `c` marks a carriageway, `b` a
/// busy one (a trunk road, four lanes or more, a dual carriageway).
/// </summary>
public static class CrossingMainRoads
{
    // The junctions a Path from S to T passes, by name (a junction's corners are one junction), and
    // the crossings it makes: their kind and whether the road is busy.
    private static (string Junctions, string Crossings, double Cost) Route(
        string map, Dictionary<char, CrossingKind>? points = null, Func<AsciiNetwork, NetworkSlice>? change = null)
    {
        var drawn = AsciiNetwork.Parse(map);
        var before = change?.Invoke(drawn) ?? drawn.Slice;
        var slice = Corners.Split(before, (points ?? []).ToDictionary(p => drawn.Node(p.Key), p => p.Value));
        int At(char name) => Enumerable.Range(0, slice.NodeCount).First(n => slice.Node(n) == drawn.Slice.Node(drawn.Node(name)));
        char NameOf(int node) => map.First(ch => char.IsAsciiLetterUpper(ch) && drawn.Slice.Node(drawn.Node(ch)) == slice.Node(node));
        var path = PathFinder.Find(slice, PathEnd.AtNode(At('S')), PathEnd.AtNode(At('T')), new RunCostModel(ScoringWeights.Default))
            ?? throw new AssertionException("No Path from S to T.");
        var junctions = new List<char> { 'S' };
        var crossings = new List<(CrossingKind, bool)>();
        foreach (var t in path.Traversals)
        {
            var segment = slice.Segments[t.Segment];
            if (segment.IsCrossingLink) crossings.Add((segment.Crossing!.Value, segment.Busy));
            var name = NameOf(t.Forward ? segment.To : segment.From);
            if (name != junctions[^1]) junctions.Add(name);
        }
        return (new string([.. junctions]), string.Join(", ", crossings.Select(c => c.Item1 + (c.Item2 ? " busy" : ""))), path.Cost);
    }

    [TestFixture]
    public class Given_side_roads_either_side_of_a_Main_Road_at_one_junction
    {
        [Test]
        public void Then_going_from_one_to_the_other_crosses_it_unmarked() =>
            Assert.That(Route("""
                      S
                      #
                W==c==J==c==E
                      #
                      T
                """).Crossings, Is.EqualTo("Unmarked"));
    }

    [TestFixture]
    public class Given_side_roads_on_the_same_side_of_a_Main_Road
    {
        //  Joined and left on one side: along its pavement, nothing crossed.
        [Test]
        public void Then_going_along_it_between_them_crosses_nothing() =>
            Assert.That(Route("""
                   S     T
                   #     #
                Wc=J==c==K=cE
                """) is var r ? (r.Junctions, r.Crossings) : default, Is.EqualTo(("SJKT", "")));
    }

    [TestFixture]
    public class Given_side_roads_on_opposite_sides_at_different_junctions
    {
        //  Along the road from one side road to the other: it's still crossed, once.
        [Test]
        public void Then_going_along_it_between_them_crosses_it_once() =>
            Assert.That(Route("""
                         T
                         #
                Wc=J==c==K=cE
                   #
                   S
                """).Crossings, Is.EqualTo("Unmarked"));
    }

    [TestFixture]
    public class Given_a_busy_road_with_signals_along_it
    {
        //  Straight across at J, or 100 m along to the lights at X and back: the lights.
        [Test]
        public void Then_it_crosses_at_the_signals() =>
            Assert.That(Route("""
                      S=====P
                      #     #
                W==b==J==b==X==b==E
                      #     #
                      T=====Q
                """, new() { ['X'] = CrossingKind.Signals }) is var r ? (r.Junctions, r.Crossings) : default, Is.EqualTo(("SPXQT", "Signals busy")));
    }

    [TestFixture]
    public class Given_a_road_no_one_may_walk_along_with_signals_on_it
    {
        //  foot=no, as on many dual carriageways: crossed only where a crossing is mapped.
        [Test]
        public void Then_it_s_crossed_only_at_the_signals() =>
            Assert.That(Route("""
                      S=====P
                      #     #
                W=xb==J=xb==X=xb==E
                      #     #
                      T=====Q
                """, new() { ['X'] = CrossingKind.Signals }) is var r ? (r.Junctions, r.Crossings) : default, Is.EqualTo(("SPXQT", "Signals busy")));
    }

    [TestFixture]
    public class Given_a_busy_road_with_no_crossing_and_no_other_way
    {
        //  Charged heavily, never forbidden: with no other way, a route is still found, and says so.
        [Test]
        public void Then_it_crosses_it_unmarked() =>
            Assert.That(Route("""
                      S
                      #
                W==b==J==b==E
                      #
                      T
                """).Crossings, Is.EqualTo("Unmarked busy"));
    }

    [TestFixture]
    public class Given_a_Main_Road_that_ends_at_a_junction
    {
        //  Round the end of the road, not across it.
        [Test]
        public void Then_going_round_its_end_crosses_nothing() =>
            Assert.That(Route("""
                S
                #
                J==b==E
                #
                T
                """).Crossings, Is.Empty);
    }

    [TestFixture]
    public class Given_a_dual_carriageway
    {
        //  Two carriageways with a road between: the middle is a side of each, so both are crossed.
        [Test]
        public void Then_both_carriageways_are_crossed() =>
            Assert.That(Route("""
                      S
                      #
                W==b==J==b==E
                      #
                V==b==K==b==F
                      #
                      T
                """).Crossings, Is.EqualTo("Unmarked busy, Unmarked busy"));
    }

    [TestFixture]
    public class Given_a_crossing_way_over_a_Main_Road
    {
        //  A zebra mapped as a path across the road (S-J-T), cut by it: the crossing is charged
        //  once, as a zebra, not as the path's and again as the junction's.
        private static NetworkSlice Zebra(AsciiNetwork drawn) => new(
            [.. Enumerable.Range(0, drawn.Slice.NodeCount).Select(drawn.Slice.Node)],
            [.. drawn.Slice.Segments.Select((s, i) => i == drawn.Segment("S-J") || i == drawn.Segment("J-T")
                ? s with { Crossing = CrossingKind.Marked, CrossesMainRoad = true, CrossingShare = 0.5 }
                : s)]);

        private const string Map = """
                  S
                  |
            W==c==J==c==E
                  |
                  T
            """;

        [Test]
        public void Then_it_s_one_crossing_a_zebra() =>
            Assert.That(Route(Map, change: Zebra).Crossings, Is.EqualTo("Marked"));

        [Test]
        public void Then_it_costs_one_zebra()
        {
            var weights = ScoringWeights.Default;
            //  The same Path but for the crossing's kind: the difference is an unmarked one's cost less a zebra's.
            Assert.That(Route(Map).Cost - Route(Map, change: Zebra).Cost,
                Is.EqualTo(weights.OffRoadPerMetre * (weights.Crossings!.MainRoadUnmarked - weights.Crossings.MainRoadMarked)).Within(1e-6));
        }
    }
}
