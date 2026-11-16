using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Crossings (CONTEXT.md; PLAN T044): crossing a road costs a fixed amount, by its kind, in metres
/// of Off-road the user would run to avoid it (user, 2026-10-23): a quiet road unmarked 20, marked
/// 20, signals 60; a Main Road signals 100, marked 80, unmarked 600 (300 till 2026-11-15, T145: "generally I don't like
/// sprinting across main roads"); a busy Main Road unmarked 5,000. A crossing way cut into Segments
/// shares its cost among them by length.
/// </summary>
public class CostingCrossings
{
    //  20 m of a crossing way, Traffic-free, wholly or partly the given crossing.
    private static Segment Crossing(CrossingKind? kind, bool mainRoad, double share = 1) =>
        new(new SegmentKey(1, 1, 2), 0, 1, 20, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0,
            Crossing: kind, CrossesMainRoad: mainRoad, CrossingShare: kind is null ? 0 : share);

    private static double Cost(Segment segment) => new RunCostModel(ScoringWeights.Default).CostOf(segment, forward: true);

    [TestCase(CrossingKind.Unmarked, false, 20)]
    [TestCase(CrossingKind.Marked, false, 20)]
    [TestCase(CrossingKind.Signals, false, 60)]
    [TestCase(CrossingKind.Unmarked, true, 600)]
    [TestCase(CrossingKind.Marked, true, 80)]
    [TestCase(CrossingKind.Signals, true, 100)]
    public void A_Crossing_costs_its_ground_and_its_kind(CrossingKind kind, bool mainRoad, double metres) =>
        Assert.That(Cost(Crossing(kind, mainRoad)), Is.EqualTo((20 * 1.1) + metres).Within(1e-9));

    [Test]
    public void Half_a_crossing_way_costs_half_its_Crossing() =>
        Assert.That(Cost(Crossing(CrossingKind.Unmarked, mainRoad: true, share: 0.5)), Is.EqualTo((20 * 1.1) + 300).Within(1e-9));

    //  T145: a busy road crossed with no crossing point, the most; marked or signalled, as any Main Road.
    [TestCase(CrossingKind.Unmarked, 5000)]
    [TestCase(CrossingKind.Marked, 80)]
    [TestCase(CrossingKind.Signals, 100)]
    public void A_busy_Main_Road_s_Crossing_costs_most_unmarked(CrossingKind kind, double metres) =>
        Assert.That(Cost(Crossing(kind, mainRoad: true) with { Busy = true }), Is.EqualTo((20 * 1.1) + metres).Within(1e-9));

    [Test]
    public void A_path_that_isn_t_a_crossing_way_costs_only_its_ground() =>
        Assert.That(Cost(Crossing(null, mainRoad: false)), Is.EqualTo(20 * 1.1).Within(1e-9));

    //  From S to A across a Main Road: straight over, unmarked, or 60 m along the kerb to lights.
    [Test]
    public void The_router_goes_along_to_the_lights_rather_than_straight_over_a_Main_Road()
    {
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(0, 20), new NodePoint(60, 0), new NodePoint(60, 20)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 20, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0,
                Crossing: CrossingKind.Unmarked, CrossesMainRoad: true, CrossingShare: 1),
            new Segment(new SegmentKey(2, 1, 3), 0, 2, 60, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 1),
            new Segment(new SegmentKey(3, 3, 4), 2, 3, 20, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 2,
                Crossing: CrossingKind.Signals, CrossesMainRoad: true, CrossingShare: 1),
            new Segment(new SegmentKey(4, 4, 2), 3, 1, 60, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 3),
        ]);
        var path = PathFinder.Find(slice, PathEnd.AtNode(0), PathEnd.AtNode(1), new RunCostModel(ScoringWeights.Default));
        Assert.That(path!.Traversals.Select(t => t.Segment), Is.EqualTo(new[] { 1, 2, 3 }));
    }
}

/// <summary>The Breakdown counts a Loop's Crossings, and those of Main Roads (R3.1; T044).</summary>
public class BreakingDownCrossings
{
    //  Round a 240 m Loop: over a Main Road by a crossing way in two halves (way 7), along, over a
    //  quiet road (way 8), and back.
    [Test]
    public void A_Loops_Breakdown_counts_its_Crossings_once_each()
    {
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(0, 10), new NodePoint(0, 20), new NodePoint(100, 20), new NodePoint(100, 0)],
        [
            new Segment(new SegmentKey(7, 1, 2), 0, 1, 10, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0,
                Crossing: CrossingKind.Signals, CrossesMainRoad: true, CrossingShare: 0.5),
            new Segment(new SegmentKey(7, 2, 3), 1, 2, 10, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 1,
                Crossing: CrossingKind.Signals, CrossesMainRoad: true, CrossingShare: 0.5),
            new Segment(new SegmentKey(1, 3, 4), 2, 3, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 2),
            new Segment(new SegmentKey(8, 4, 5), 3, 4, 20, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 3,
                Crossing: CrossingKind.Unmarked, CrossesMainRoad: false, CrossingShare: 1),
            new Segment(new SegmentKey(2, 5, 1), 4, 0, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 4),
        ]);
        var loop = new WendYourWay.Core.Loops.Loop(0, [], [.. Enumerable.Range(0, 5).Select(i => new Traversal(i, true))]);
        var breakdown = Breakdown.Of(slice, loop);
        Assert.That((breakdown.Crossings, breakdown.MainRoadCrossings), Is.EqualTo((2, 1)));
    }
}
