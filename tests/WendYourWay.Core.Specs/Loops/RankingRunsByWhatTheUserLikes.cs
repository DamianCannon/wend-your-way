using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// A Run's Loops ranked as the user's stars weigh them (PLAN T105; user: "on balance the blend runs
/// look a little better so yes let's go"): the score, plus 0.380 for each whole of it off-road and
/// 0.0132 for each metre it climbs per km (T104's fit). Rides, Trail Rides and point-to-points are
/// ranked as before; the app's stars are drawn from the score alone.
/// </summary>
public class RankingRunsByWhatTheUserLikes
{
    //  2 km square, a junction every 100 m: flat Off-road west of the middle, Traffic-free east of
    //  it climbing 6 m up and down every 100 m (60 m per km). The score alone prefers the west; the
    //  blend, the east's climbing.
    private static readonly NetworkSlice HillyEast = GridNetwork.Create(junctionsAcross: 21,
        ground: at => at.X < 1000 ? GroundKind.OffRoad : GroundKind.TrafficFree,
        climbing: at => at.X < 1000 ? 0 : 6);

    private static double ClimbPerKmOfLoop1(Activity activity) =>
        Breakdown.Of(HillyEast, LoopGenerator.Offer(HillyEast, new Request(new NodePoint(1000, 1000), DistanceKm: 4, Activity: activity), ScoringWeights.Default)[0]).ClimbPerKm;

    [Test]
    public void A_Run_s_Loop_1_is_the_hilly_one_the_blend_prefers() =>
        Assert.That(ClimbPerKmOfLoop1(Activity.Run), Is.GreaterThan(40));

    [Test]
    public void A_Gravel_Ride_s_Loop_1_is_the_one_the_score_prefers() =>
        Assert.That(ClimbPerKmOfLoop1(Activity.GravelRide), Is.LessThan(20));

    [Test]
    public void A_Run_is_ranked_with_the_blend() =>
        Assert.That((ScoringWeights.Default.Ranking(Activity.Run).OffRoadShareWeight, ScoringWeights.Default.Ranking(Activity.Run).ClimbPerKmWeight), Is.EqualTo((0.380, 0.0132)));

    [TestCase(Activity.GravelRide)]
    [TestCase(Activity.TrailRide)]
    public void A_ride_is_ranked_as_before(Activity activity) =>
        Assert.That(ScoringWeights.Default.Ranking(activity), Is.EqualTo(ScoringWeights.Default.For(activity)));

    //  The stars are drawn from For: the score alone.
    [Test]
    public void The_score_for_the_stars_has_no_blend() =>
        Assert.That((ScoringWeights.Default.For(Activity.Run).OffRoadShareWeight, ScoringWeights.Default.For(Activity.Run).ClimbPerKmWeight), Is.EqualTo((0.0, 0.0)));
}
