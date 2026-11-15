using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// The Off-road Lean (PLAN T058, T058b): from −1 (tarmac) to +1 (trails), 0 its centre. Leaning to
/// tarmac an Off-road metre costs 1 + its weight × the Lean times as much; leaning to trails it costs
/// 1 − the trails weight × the Lean times as much, so roads stay as far behind green ground.
/// </summary>
public class LeaningOnOffRoad
{
    //  100 m of the given ground, nothing beside it.
    private static Segment Path(GroundKind ground) =>
        new(new SegmentKey(1, 1, 2), 0, 1, 100, ground, AccessKind.Allowed, SameGroundGroup: 0);

    private static double Cost(double lean, GroundKind ground) =>
        new RunCostModel(ScoringWeights.Default with { OffRoadLean = lean, OffRoadLeanWeight = 0.5, TrailsLeanWeight = 0.4 }).CostOf(Path(ground), forward: true);

    private static double Rate(GroundKind ground) => ground switch
    {
        GroundKind.OffRoad => ScoringWeights.Default.OffRoadPerMetre,
        GroundKind.TrafficFree => ScoringWeights.Default.TrafficFreePerMetre,
        _ => ScoringWeights.Default.ConnectorPerMetre,
    };

    [Test]
    public void Leaning_to_trails_an_Off_road_path_costs_less() =>
        Assert.That(Cost(1, GroundKind.OffRoad), Is.EqualTo(100 * Rate(GroundKind.OffRoad) * 0.6).Within(1e-9));

    [Test]
    public void Leaning_to_tarmac_an_Off_road_path_costs_more() =>
        Assert.That(Cost(-1, GroundKind.OffRoad), Is.EqualTo(100 * Rate(GroundKind.OffRoad) * 1.5).Within(1e-9));

    [TestCase(0.5, 0.8, TestName = "Leaning_half_way_to_trails_moves_the_cost_half_as_far")]
    [TestCase(-0.5, 1.25, TestName = "Leaning_half_way_to_tarmac_moves_the_cost_half_as_far")]
    public void Leaning_half_way_moves_the_cost_half_as_far(double lean, double factor) =>
        Assert.That(Cost(lean, GroundKind.OffRoad), Is.EqualTo(100 * Rate(GroundKind.OffRoad) * factor).Within(1e-9));

    //  Only Off-road moves: Traffic-free and roads cost their rate whichever way.
    [TestCase(1.0, GroundKind.TrafficFree)]
    [TestCase(-1.0, GroundKind.TrafficFree)]
    [TestCase(1.0, GroundKind.Connector)]
    [TestCase(-1.0, GroundKind.Connector)]
    public void Traffic_free_and_roads_cost_their_rate(double lean, GroundKind ground) =>
        Assert.That(Cost(lean, ground), Is.EqualTo(100 * Rate(ground)).Within(1e-9));

    //  A*'s floor allows for the cheapest a metre can be: Off-road leant to, beside green, on a Corridor.
    [Test]
    public void Leaning_to_trails_the_floor_allows_for_the_cheaper_Off_road()
    {
        var weights = ScoringWeights.Default with { OffRoadLean = 1, TrailsLeanWeight = 0.4 };
        var cheapest = new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0, Greenery: 1, Corridor: "Capital Ring");
        Assert.That(new RunCostModel(weights).FloorPerMetre, Is.LessThanOrEqualTo(new RunCostModel(weights).CostOf(cheapest, true) / 100 + 1e-12));
    }

    [TestCase(GroundKind.OffRoad)]
    [TestCase(GroundKind.TrafficFree)]
    public void By_default_nothing_leans(GroundKind ground) =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(Path(ground), forward: true), Is.EqualTo(100 * Rate(ground)).Within(1e-9));
}

/// <summary>
/// The Default weights' Off-road Lean never makes green ground cost more than an ordinary road (T058;
/// ADR 0007, a Lean can't push road into a Loop).
/// </summary>
public class LeaningOnOffRoadKeepsLoopsGreen
{
    private static double PerMetre(double lean, GroundKind ground) =>
        new RunCostModel(ScoringWeights.Default with { OffRoadLean = lean }).CostOf(
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, ground, AccessKind.Allowed, SameGroundGroup: 0), forward: true) / 100;

    //  A metre always costs something, so a Loop's score stays finite.
    [Test]
    public void Leaning_fully_to_trails_an_Off_road_path_still_costs_something() =>
        Assert.That(PerMetre(1, GroundKind.OffRoad), Is.GreaterThan(0));

    [Test]
    public void Leaning_fully_to_tarmac_an_Off_road_path_costs_less_than_a_road() =>
        Assert.That(PerMetre(-1, GroundKind.OffRoad), Is.LessThan(PerMetre(-1, GroundKind.Connector)));
}
