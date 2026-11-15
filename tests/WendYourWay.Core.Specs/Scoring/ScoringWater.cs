using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Water (PLAN T043): the user's best Loops follow canals and rivers ("gets a lot of canal",
/// "stick with the river"), so a Loop's score rises by half its share beside water, as Flow lifts
/// it; the router already treats water as green through Greenery.
/// </summary>
public class ScoringWater
{
    //  A 400 m ring of Off-road, too short for Flow, its Segments the given share beside water.
    private static (NetworkSlice Slice, Loop Loop) Ring(double waterside)
    {
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(50, 0), new NodePoint(50, 50), new NodePoint(0, 50)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0, Waterside: waterside),
            new Segment(new SegmentKey(2, 2, 3), 1, 2, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 1, Waterside: waterside),
            new Segment(new SegmentKey(3, 3, 4), 2, 3, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 2, Waterside: waterside),
            new Segment(new SegmentKey(4, 4, 1), 3, 0, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 3, Waterside: waterside),
        ]);
        return (slice, new Loop(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, true), new Traversal(3, true)]));
    }

    private static double Score(double waterside)
    {
        var (slice, loop) = Ring(waterside);
        return LoopScorer.Score(slice, loop, ScoringWeights.Default);
    }

    [Test]
    public void A_Loop_wholly_beside_water_scores_half_as_much_again() =>
        Assert.That(Score(1) / Score(0), Is.EqualTo(1.5).Within(1e-12));

    [Test]
    public void A_Loop_half_beside_water_scores_a_quarter_more() =>
        Assert.That(Score(0.5) / Score(0), Is.EqualTo(1.25).Within(1e-12));

    [Test]
    public void A_Loops_Breakdown_gives_its_share_beside_water()
    {
        var (slice, loop) = Ring(0.4);
        Assert.That(Breakdown.Of(slice, loop).WatersidePercent, Is.EqualTo(40).Within(1e-9));
    }
}

/// <summary>
/// Riverside pavements (PLAN T045): the Embankment's pavement beside the Thames is the river's, not
/// the busy road's on its other side ("stick with the river to Westminster Bridge", user).
/// </summary>
public class CostingRiversidePavements
{
    //  100 m of pavement beside a Main Road (lying on the road's same-ground group and its own),
    //  the given share of it beside water.
    private static Segment Pavement(double waterside) =>
        new(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.Connector, AccessKind.Allowed,
            [new GroundShare(7, 100), new GroundShare(8, 100)], MainRoad: true, Waterside: waterside);

    private static double Cost(Segment segment) => new RunCostModel(ScoringWeights.Default).CostOf(segment, forward: true);

    [Test]
    public void A_Main_Roads_pavement_beside_a_river_costs_an_ordinary_pavements_rate() =>
        Assert.That(Cost(Pavement(waterside: 1)), Is.EqualTo(100 * 3.0).Within(1e-9));

    [Test]
    public void A_Main_Roads_pavement_away_from_water_is_still_a_Main_Road() =>
        Assert.That(Cost(Pavement(waterside: 0.2)), Is.EqualTo(100 * 15.0).Within(1e-9));

    [Test]
    public void A_Main_Road_itself_beside_a_river_is_still_a_Main_Road() =>
        Assert.That(Cost(new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 7,
            MainRoad: true, Waterside: 1)), Is.EqualTo(100 * 15.0).Within(1e-9));
}
