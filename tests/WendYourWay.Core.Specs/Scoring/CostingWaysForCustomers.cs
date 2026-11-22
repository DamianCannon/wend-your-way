using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// A way for customers only (T182, #186): used, as OSM's tagging of it is too often wrong to rule it out,
/// but costing a little more, a gate's worth (user: "alright a little more"), so a Loop takes it when it's
/// the natural way and doesn't seek it out.
/// </summary>
public class CostingWaysForCustomers
{
    private static Segment Path(bool customers) =>
        new(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0) { Customers = customers };

    private static double Cost(Segment segment) => new RunCostModel(ScoringWeights.Default).CostOf(segment, forward: true);

    [Test]
    public void A_way_for_customers_costs_a_gate_s_worth_more() =>
        Assert.That(Cost(Path(customers: true)) - Cost(Path(customers: false)), Is.EqualTo(30 * 1.0).Within(1e-9));

    [Test]
    public void The_same_way_open_to_all_costs_its_ground_alone() =>
        Assert.That(Cost(Path(customers: false)), Is.EqualTo(100 * 1.0).Within(1e-9));
}
