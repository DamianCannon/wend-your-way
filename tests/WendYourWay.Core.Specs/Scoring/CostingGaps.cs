using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// A gap in the map (#150, T146a) costs its path's ground and a fixed amount more, as many metres
/// of Off-road as <see cref="RunCostModel.GapM"/>, so a Loop takes one only for a real gain.
/// </summary>
public class CostingGaps
{
    private static Segment Gap(MapGapKind kind) =>
        new(new SegmentKey(1, 1, 2), 0, 1, 3, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0) { MapGap = kind };

    [Test]
    public void A_gap_costs_its_ground_and_30_m_of_Off_road_more() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(Gap(MapGapKind.Joined), forward: true), Is.EqualTo((3 * 1.1) + (30 * 1.0)).Within(1e-9));

    [Test]
    public void A_path_costs_its_ground_alone() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(Gap(MapGapKind.None), forward: true), Is.EqualTo(3 * 1.1).Within(1e-9));

    //  T146c: a Loop over a possible gap the user included scores as one over a joined gap would, not as
    //  over a forbidden way (a Main Road's cost): a 200 m path out, back over the 8 m gap and a road.
    [Test]
    public void A_Loop_over_an_included_possible_gap_scores_as_over_a_joined_one() =>
        Assert.That(ScoreOver(MapGapKind.Possible, AccessKind.Forbidden), Is.EqualTo(ScoreOver(MapGapKind.Joined, AccessKind.Allowed)).Within(1e-9));

    private static double ScoreOver(MapGapKind kind, AccessKind access)
    {
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(200, 0), new NodePoint(200, 8)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 200, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0),
            new Segment(new SegmentKey(1, 2, 3), 1, 2, 8, GroundKind.TrafficFree, access, SameGroundGroup: 1) { MapGap = kind },
            new Segment(new SegmentKey(2, 3, 1), 2, 0, 201, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 2),
        ]);
        return LoopScorer.Score(slice, new Loop(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, true)]), ScoringWeights.Default);
    }
}
