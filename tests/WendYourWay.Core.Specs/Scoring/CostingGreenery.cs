using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Greenery (CONTEXT.md; PLAN T036): a tarmac path through a park is better than one beside a
/// dual carriageway, though both are Traffic-free. A Segment's cost per metre is cut by up to
/// 30% for the share of it within 15 m of green areas, on top of its ground.
/// </summary>
public class CostingGreenery
{
    private static Segment Path(double greenery) =>
        new(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0, Greenery: greenery);

    [Test]
    public void A_path_wholly_in_green_costs_30_percent_less() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(Path(1), forward: true), Is.EqualTo(100 * 1.1 * 0.7).Within(1e-9));

    [Test]
    public void A_path_with_no_green_beside_it_costs_its_grounds_rate() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(Path(0), forward: true), Is.EqualTo(100 * 1.1).Within(1e-9));

    //  A* relies on no cost falling below the floor: green Off-road is now the cheapest ground.
    [Test]
    public void The_floor_allows_for_the_cheapest_green_ground() =>
        Assert.That(new RunCostModel(ScoringWeights.Default with { CorridorDiscount = 0 }).FloorPerMetre, Is.EqualTo(1.0 * 0.7).Within(1e-12));

    [Test]
    public void A_Loops_Breakdown_gives_its_Greenery_by_length()
    {
        //  300 m wholly green out, 100 m bare back.
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(100, 0)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 300, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0, Greenery: 1),
            new Segment(new SegmentKey(2, 2, 1), 1, 0, 100, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 1, Greenery: 0),
        ]);
        var loop = new Loop(0, [], [new Traversal(0, true), new Traversal(1, true)]);
        Assert.That(Breakdown.Of(slice, loop).GreeneryPercent, Is.EqualTo(75).Within(1e-9));
    }

    //  "Never along a main road" (user, R2.1): green beside it doesn't make it any better.
    [Test]
    public void A_Main_Road_beside_green_areas_costs_its_full_rate() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 0, MainRoad: true, Greenery: 1), forward: true),
            Is.EqualTo(100 * 15.0).Within(1e-9));
}
