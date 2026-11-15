using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// A Loop's score is its cost per km under the router's cost model, expressed against the
/// cheapest ground: 1 for a Loop wholly on it with nothing repeated, and less for everything else.
/// But it judges the whole Loop as the user does (T042, from their rated Loops): a Main Road link
/// costs an ordinary road's rate, ground covered again twice its rate, and each Knot takes 15% off,
/// where the router charges a Main Road five times a road and ground used before four times.
/// </summary>
public static class ScoringALoop
{
    private static double Score(string map, string walk, params string[] sameGround) =>
        Score(ScoringWeights.Default, map, walk, sameGround);

    private static double Score(ScoringWeights weights, string map, string walk, params string[] sameGround)
    {
        var network = AsciiNetwork.Parse(map, sameGround);
        return LoopScorer.Score(network.Slice, network.LoopThrough(walk), weights);
    }

    //  300 m of road (S=A), 200 m Traffic-free (A|C), 500 m Off-road (C.B:S).
    private const string MixedLoop = """
        S=====A
        :     |
        B.....C
        """;

    [TestFixture]
    public class A_Loop_wholly_Off_road
    {
        [Test]
        public void Then_it_scores_1() =>
            Assert.That(Score("""
                S.....A
                :     :
                B.....C
                """, "SACBS"), Is.EqualTo(1).Within(1e-12));
    }

    [TestFixture]
    public class An_out_and_back_Loop
    {
        //  Out along S.A and back the same way: the way back is a Retrace, at twice its ground's
        //  rate. (1 + 2) / 2 per metre scores 2/3.
        [Test]
        public void Then_its_Retrace_costs_twice_its_ground() =>
            Assert.That(Score("S.....A", "SAS"), Is.EqualTo(2 / 3.0).Within(1e-12));

        //  Coming back along a separately mapped path on the same ground is still a Retrace.
        [Test]
        public void Then_returning_on_the_same_ground_by_another_way_is_a_Retrace_too() =>
            Assert.That(Score("""
                S.....A
                :     :
                B.....C
                """, "SACBS", "S-A B-C"), Is.EqualTo(1000 / 1300.0).Within(1e-12));
    }

    [TestFixture]
    public class Loops_of_the_same_make_up_but_different_lengths
    {
        //  Twice the size in every direction, the same shares of each ground (R2.9).
        private const string TwiceTheSize = """
            S===========A
            :           |
            :           |
            :           |
            B...........C
            """;

        [Test]
        public void Then_they_score_the_same() =>
            Assert.That(Score(TwiceTheSize, "SACBS"), Is.EqualTo(Score(MixedLoop, "SACBS")).Within(1e-12));
    }

    [TestFixture]
    public class A_Loop_with_more_Off_road
    {
        //  As the mixed Loop, but the road S=A is an Off-road path S.A instead (R2.2).
        [Test]
        public void Then_it_scores_higher() =>
            Assert.That(Score("""
                S.....A
                :     |
                B.....C
                """, "SACBS"), Is.GreaterThan(Score(MixedLoop, "SACBS")));
    }

    [TestFixture]
    public class The_score_and_the_router_share_their_weights
    {
        //  300 × 3 + 200 × 1.1 + 500 × 1 = 1,620 cost units over 1,000 m (no Flow: too short).
        [Test]
        public void Then_the_score_is_the_cheapest_rate_over_the_Loops_rate() =>
            Assert.That(Score(MixedLoop, "SACBS"), Is.EqualTo(1000 / 1620.0).Within(1e-12));

        //  Doubling the cost of a metre of road changes the score (R2.10): 300 × 6 + 220 + 500.
        [Test]
        public void Then_a_dearer_road_lowers_the_score() =>
            Assert.That(
                Score(ScoringWeights.Default with { ConnectorPerMetre = 6 }, MixedLoop, "SACBS"),
                Is.EqualTo(1000 / 2520.0).Within(1e-12));
    }

    [TestFixture]
    public class A_Loop_back_along_a_pavement_partly_beside_its_road
    {
        //  400 m of Connector at 3 a metre, twice that on only the 180 m of pavement beside road
        //  already covered: 3 × (400 + 180) = 1,740 against a floor of 400.
        [Test]
        public void Then_only_the_metres_beside_the_road_cost_the_reused_ground_rate()
        {
            var (slice, loop) = PavementBackAlongARoad.Build();
            Assert.That(LoopScorer.Score(slice, loop, ScoringWeights.Default), Is.EqualTo(400.0 / 1740).Within(1e-12));
        }
    }

    //  The router never takes a forbidden Segment, but a route the user ran or rated may: 6 m of
    //  private road made one rated Loop score 0, leaving the guard on the rest (T037 review). It
    //  costs what a Main Road does in the score: 300 m Off-road at 1 and 100 m forbidden at 3 = 600.
    [TestFixture]
    public class A_route_over_a_forbidden_Segment
    {
        [Test]
        public void Then_it_scores_as_if_that_Segment_were_a_Main_Road()
        {
            var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(50, 0), new NodePoint(50, 50), new NodePoint(0, 50)],
            [
                new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0),
                new Segment(new SegmentKey(2, 2, 3), 1, 2, 100, GroundKind.OffRoad, AccessKind.Forbidden, SameGroundGroup: 1),
                new Segment(new SegmentKey(3, 3, 4), 2, 3, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 2),
                new Segment(new SegmentKey(4, 4, 1), 3, 0, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 3),
            ]);
            var loop = new Loop(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, true), new Traversal(3, true)]);
            Assert.That(LoopScorer.Score(slice, loop, ScoringWeights.Default), Is.EqualTo(400.0 / 600).Within(1e-12));
        }
    }

    //  The user's very good Loops took a Main Road link to reach a park ("worth it"): judged as a
    //  whole, it costs what an ordinary road does. The router still keeps off it.
    [TestFixture]
    public class A_Loop_with_a_Main_Road_link
    {
        [Test]
        public void Then_it_scores_as_if_the_link_were_an_ordinary_road() =>
            Assert.That(Score("""
                S==m==A
                :     |
                B.....C
                """, "SACBS"), Is.EqualTo(Score(MixedLoop, "SACBS")).Within(1e-12));
    }

    //  Round the ring with a spur out to D and back: one Knot, 15% off.
    [TestFixture]
    public class A_Loop_with_a_Knot
    {
        private const string RingWithASpur = """
            S.....A.....D
            :     :
            B.....C
            """;

        [Test]
        public void Then_it_scores_15_percent_less() =>
            Assert.That(Score(RingWithASpur, "SADACBS"),
                Is.EqualTo(0.85 * Score(ScoringWeights.Default with { KnotPenalty = 0 }, RingWithASpur, "SADACBS")).Within(1e-12));
    }
}
