using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Ground a Loop has already used costs four times its ground's rate (R2.4), whatever made it
/// cheap the first time: discounted, a green Corridor retraced cost less than a fresh quiet street,
/// and later Paths doubled back along it (review after T037).
/// </summary>
public class CostingReusedGround
{
    //  A Traffic-free path wholly beside green areas, on a Corridor: 1.1 × 0.7 × 0.8 a metre, fresh.
    private static Segment GreenCorridor(double lengthM, params GroundShare[] sameGround) =>
        new(new SegmentKey(1, 1, 2), 0, 1, lengthM, GroundKind.TrafficFree, AccessKind.Allowed,
            sameGround.Length > 0 ? sameGround : [new GroundShare(0, lengthM)], Greenery: 1, Corridor: "Capital Ring");

    [Test]
    public void Retracing_green_Corridor_costs_four_times_its_grounds_rate() =>
        Assert.That(new RunCostModel(ScoringWeights.Default, usedGround: new HashSet<int> { 0 }).CostOf(GreenCorridor(100), forward: true),
            Is.EqualTo(100 * 1.1 * 4).Within(1e-9));

    [Test]
    public void Only_the_metres_already_used_lose_their_discounts()
    {
        //  40 m on ground already used, 60 m fresh.
        var segment = GreenCorridor(100, new GroundShare(0, 40), new GroundShare(1, 60));
        Assert.That(new RunCostModel(ScoringWeights.Default, usedGround: new HashSet<int> { 0 }).CostOf(segment, forward: true),
            Is.EqualTo((60 * 1.1 * 0.7 * 0.8) + (40 * 1.1 * 4)).Within(1e-9));
    }
}
