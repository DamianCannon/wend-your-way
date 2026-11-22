using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// A Main Road with a real pavement (T181, #185): 30 mph or under, with pavements on both sides. It
/// costs less on foot than one without: 6 a metre, not 15, still twice a quiet street's 3. The user found a
/// Loop going about 1.5 km back round a park rather than 175 m along such a road ("it absolutely refuses
/// to use the main road there").
/// </summary>
public class CostingMainRoadPavements
{
    private static Segment MainRoad(bool pavement) =>
        new(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 0, MainRoad: true) { Pavement = pavement };

    private static double Cost(Segment segment) => new RunCostModel(ScoringWeights.Default).CostOf(segment, forward: true);

    [Test]
    public void A_Main_Road_with_a_real_pavement_costs_6_a_metre() =>
        Assert.That(Cost(MainRoad(pavement: true)), Is.EqualTo(100 * 6.0).Within(1e-9));

    [Test]
    public void A_Main_Road_without_one_still_costs_15() =>
        Assert.That(Cost(MainRoad(pavement: false)), Is.EqualTo(100 * 15.0).Within(1e-9));

    //  A pavement says nothing about a quiet street, which costs what it did.
    [Test]
    public void A_quiet_street_costs_the_same_with_or_without() =>
        Assert.That(Cost(MainRoad(pavement: true) with { MainRoad = false }), Is.EqualTo(100 * 3.0).Within(1e-9));

    //  The score judges a whole Loop's Main Road at a quiet street's 3 (T042): a pavement never makes it dearer.
    [Test]
    public void A_whole_Loop_s_Main_Road_with_a_pavement_is_judged_no_dearer() =>
        Assert.That(ScoringWeights.Default with { MainRoadPerMetre = ScoringWeights.Default.LoopMainRoadPerMetre } is var judging
                    && new RunCostModel(judging).CostOf(MainRoad(pavement: true), forward: true) <= new RunCostModel(judging).CostOf(MainRoad(pavement: false), forward: true),
            Is.True);
}
