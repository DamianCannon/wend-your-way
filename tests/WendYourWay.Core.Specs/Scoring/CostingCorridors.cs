using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Corridors (CONTEXT.md; PLAN T037): green ground on a walking route, or a national or
/// international cycle route, such as the Capital Ring. A metre of it costs a further 20% less,
/// on top of its ground and Greenery.
/// </summary>
public class CostingCorridors
{
    private static Segment Path(string? corridor) =>
        new(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0, Corridor: corridor);

    [Test]
    public void A_path_on_a_Corridor_costs_20_percent_less() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(Path("Capital Ring"), forward: true), Is.EqualTo(100 * 1.1 * 0.8).Within(1e-9));

    [Test]
    public void A_path_on_no_Corridor_costs_its_grounds_rate() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(Path(null), forward: true), Is.EqualTo(100 * 1.1).Within(1e-9));

    //  A* relies on no cost falling below the floor: green Off-road on a Corridor is the cheapest.
    [Test]
    public void The_floor_allows_for_the_cheapest_green_Corridor() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).FloorPerMetre, Is.EqualTo(1.0 * 0.7 * 0.8).Within(1e-12));

    [Test]
    public void A_Loops_Breakdown_gives_its_length_on_Corridors_and_the_one_it_follows_longest()
    {
        //  300 m of the Capital Ring, 200 m of the Thames Path, 100 m more of the Capital Ring,
        //  then 100 m on no Corridor back to the start.
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(50, 0), new NodePoint(50, 50), new NodePoint(0, 50)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 300, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0, Corridor: "Capital Ring"),
            new Segment(new SegmentKey(2, 2, 3), 1, 2, 200, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 1, Corridor: "Thames Path"),
            new Segment(new SegmentKey(3, 3, 4), 2, 3, 100, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 2, Corridor: "Capital Ring"),
            new Segment(new SegmentKey(4, 4, 1), 3, 0, 100, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 3),
        ]);
        var loop = new Loop(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, true), new Traversal(3, true)]);
        var breakdown = Breakdown.Of(slice, loop);
        Assert.That((breakdown.CorridorLengthM, breakdown.MainCorridor, breakdown.MainCorridorM), Is.EqualTo((600.0, "Capital Ring", 400.0)));
    }

    //  A Corridor is green ground: a pavement the build found beside a road, though on a route, is a road.
    [Test]
    public void A_Connector_on_a_route_costs_a_Connectors_rate() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 0, Corridor: "Capital Ring"), forward: true),
            Is.EqualTo(100 * 3.0).Within(1e-9));

    [Test]
    public void A_Connector_on_a_route_isn_t_Corridor_length_in_the_Breakdown()
    {
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(50, 0)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0, Corridor: "Capital Ring"),
            new Segment(new SegmentKey(2, 2, 1), 1, 0, 100, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 1, Corridor: "Capital Ring"),
        ]);
        var loop = new Loop(0, [], [new Traversal(0, true), new Traversal(1, true)]);
        var breakdown = Breakdown.Of(slice, loop);
        Assert.That((breakdown.CorridorLengthM, breakdown.MainCorridorM), Is.EqualTo((100.0, 100.0)));
    }
}
