using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// A gap in the map (#150, T146a) costs its path's ground and a fixed amount more, as many metres
/// of Off-road as <see cref="RunCostModel.GapM"/>, so a Loop takes one only for a real gain.
/// </summary>
public class CostingGaps
{
    private static Segment Gap(GapKind kind) =>
        new(new SegmentKey(1, 1, 2), 0, 1, 3, GroundKind.TrafficFree, AccessKind.Allowed, SameGroundGroup: 0) { Gap = kind };

    [Test]
    public void A_gap_costs_its_ground_and_30_m_of_Off_road_more() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(Gap(GapKind.Joined), forward: true), Is.EqualTo((3 * 1.1) + (30 * 1.0)).Within(1e-9));

    [Test]
    public void A_path_costs_its_ground_alone() =>
        Assert.That(new RunCostModel(ScoringWeights.Default).CostOf(Gap(GapKind.None), forward: true), Is.EqualTo(3 * 1.1).Within(1e-9));
}
