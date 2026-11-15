using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// The Hills Lean (PLAN T050): from −1 (fewer hills) to +1 (more), 0 its centre. A Segment's relief
/// is its climbing up and down per metre. Leaning to fewer hills, steep ground costs more; leaning
/// to more, flat ground costs more: nothing gets cheaper, so A*'s floor stands.
/// </summary>
public class LeaningOnHills
{
    //  100 m of Off-road climbing and descending the given metres.
    private static Segment Path(double ascentM, double descentM = 0) =>
        new(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0, AscentM: ascentM, DescentM: descentM);

    private static double Cost(double lean, Segment segment) =>
        new RunCostModel(ScoringWeights.Default with { HillsLean = lean, FewerHillsCostWeight = 5, MoreHillsCostWeight = 4 }).CostOf(segment, forward: true);

    [Test]
    public void Leaning_to_fewer_hills_a_steep_path_costs_more_by_its_relief() =>
        Assert.That(Cost(-1, Path(6, 4)), Is.EqualTo(100 * (1 + (5 * 0.1))).Within(1e-9));

    //  Bounded, so a steep green path never costs more than a flat road.
    [Test]
    public void Leaning_to_fewer_hills_a_path_steeper_than_the_steepest_relief_costs_what_the_steepest_does() =>
        Assert.That(Cost(-1, Path(20)), Is.EqualTo(100 * (1 + (5 * RunCostModel.SteepestRelief))).Within(1e-9));

    [Test]
    public void Leaning_to_more_hills_a_flat_path_costs_more() =>
        Assert.That(Cost(1, Path(0)), Is.EqualTo(100 * (1 + 4)).Within(1e-9));

    [Test]
    public void Leaning_to_more_hills_a_path_half_the_steepest_relief_costs_half_as_much_more() =>
        Assert.That(Cost(1, Path(RunCostModel.SteepestRelief * 50)), Is.EqualTo(100 * (1 + (4 * 0.5))).Within(1e-9));

    //  Never cheaper than at the centre, so A*'s floor stands.
    [Test]
    public void Leaning_to_more_hills_a_path_steeper_than_the_steepest_relief_costs_what_it_does_at_the_centre() =>
        Assert.That(Cost(1, Path(RunCostModel.SteepestRelief * 300)), Is.EqualTo(100).Within(1e-9));

    //  The centre changes nothing, so the pairs guard can't move.
    [TestCase(0.0, 0.0, TestName = "At_the_centre_a_flat_path_costs_its_grounds_rate")]
    [TestCase(6.0, 4.0, TestName = "At_the_centre_a_steep_path_costs_its_grounds_rate")]
    public void At_the_centre_a_path_costs_its_grounds_rate(double ascentM, double descentM) =>
        Assert.That(Cost(0, Path(ascentM, descentM)), Is.EqualTo(100).Within(1e-9));

    [TestCase(-0.5, 6.0, 1 + (0.5 * 5 * 0.06), TestName = "Leaning_half_way_to_fewer_hills_a_steep_path_costs_half_as_much_more")]
    [TestCase(0.5, 0.0, 1 + (0.5 * 4), TestName = "Leaning_half_way_to_more_hills_a_flat_path_costs_half_as_much_more")]
    public void Leaning_half_way_moves_the_cost_half_as_far(double lean, double ascentM, double factor) =>
        Assert.That(Cost(lean, Path(ascentM)), Is.EqualTo(100 * factor).Within(1e-9));

    [Test]
    public void By_default_nothing_leans() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(Path(6, 4), forward: true), Is.EqualTo(100).Within(1e-9));

    //  A 400 m ring of Off-road, too short for Flow, each Segment climbing 2 m and dropping 1 m
    //  the way the Loop goes: 20 m of climbing per km. The score alone, without the cost's Lean.
    private static double Score(double lean)
    {
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(50, 0), new NodePoint(50, 50), new NodePoint(0, 50)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0, AscentM: 2, DescentM: 1),
            new Segment(new SegmentKey(2, 2, 3), 1, 2, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 1, AscentM: 2, DescentM: 1),
            new Segment(new SegmentKey(3, 3, 4), 2, 3, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 2, AscentM: 2, DescentM: 1),
            new Segment(new SegmentKey(4, 1, 4), 0, 3, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 3, AscentM: 1, DescentM: 2),
        ]);
        var loop = new Loop(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, true), new Traversal(3, false)]);
        return LoopScorer.Score(slice, loop, ScoringWeights.Default with { HillsLean = lean, FewerHillsCostWeight = 0, MoreHillsCostWeight = 0, HillsScoreWeight = 0.5 });
    }

    [Test]
    public void Leaning_to_more_hills_a_Loop_scores_higher_by_its_climbing_per_km() =>
        Assert.That(Score(1) / Score(0), Is.EqualTo(Math.Exp(0.5)).Within(1e-12));

    [Test]
    public void Leaning_to_fewer_hills_a_Loop_scores_lower_by_its_climbing_per_km() =>
        Assert.That(Score(-1) / Score(0), Is.EqualTo(Math.Exp(-0.5)).Within(1e-12));
}

/// <summary>
/// The Default weights' Hills Lean never makes a road cheaper than a green path (T050; ADR 0007, a
/// Lean can't push road into a Loop; user: "I never want to run along a main road").
/// </summary>
public class LeaningOnHillsKeepsLoopsGreen
{
    private static double PerMetre(double lean, GroundKind ground, double climbM) =>
        new RunCostModel(ScoringWeights.Default with { HillsLean = lean }).CostOf(
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, ground, AccessKind.Allowed, SameGroundGroup: 0, AscentM: climbM, DescentM: climbM), forward: true) / 100;

    [Test]
    public void Leaning_fully_to_fewer_hills_the_steepest_green_path_costs_less_than_a_flat_road() =>
        Assert.That(PerMetre(-1, GroundKind.TrafficFree, 20), Is.LessThan(PerMetre(-1, GroundKind.Connector, 0)));

    [Test]
    public void Leaning_fully_to_more_hills_a_flat_green_path_costs_less_than_the_steepest_road() =>
        Assert.That(PerMetre(1, GroundKind.TrafficFree, 0), Is.LessThan(PerMetre(1, GroundKind.Connector, 20)));
}
