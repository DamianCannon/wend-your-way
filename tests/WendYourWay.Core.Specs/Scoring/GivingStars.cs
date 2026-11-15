using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// The app's stars for a route (PLAN T097, STORY-045). A Run's from its ranking value, as its Loops
/// are ranked (T108: its score plus what the user's stars weigh, its share off-road and its
/// climbing per km), cut at the 10th, 30th, 50th and 70th percentiles of the user's matched Runs
/// (user: "Fifths of your Outings", then "add a 0.5 star adjustment as well ... to match me"). A
/// ride's from its score alone, a fifth of the user's rides each. None for a route matched for less
/// than 80% of its length, whose score isn't of the route.
/// </summary>
public class GivingStars
{
    //  A Run's cut points 0.1910, 0.4008, 0.5575, 0.7172 (measured 2026-11-11, T108); its ranking
    //  value its score + 0.380 x its off-road share + 0.0132 x its climbing per km.
    [TestCase(0.0, 1)]
    [TestCase(0.1909, 1)]
    [TestCase(0.1910, 2)]
    [TestCase(0.4007, 2)]
    [TestCase(0.4008, 3)]
    [TestCase(0.5574, 3)]
    [TestCase(0.5575, 4)]
    [TestCase(0.7171, 4)]
    [TestCase(0.7172, 5)]
    [TestCase(2.7, 5)]
    public void A_Runs_ranking_value_gets_the_stars_of_its_band(double score, int stars) =>
        Assert.That(Stars.Of(Activity.Run, score, matchedShare: 1, offRoadShare: 0, climbPerKm: 0), Is.EqualTo(stars));

    //  The Finsbury Park Run of 2018 (T107, the user's 4 stars): 0.0648 by its score, 41% off-road,
    //  17.1 m a km: 0.446 ranked, 3 stars, not the score's 1.
    [Test]
    public void Off_road_and_climbing_raise_a_Runs_stars() =>
        Assert.That(Stars.Of(Activity.Run, 0.0648, matchedShare: 1, offRoadShare: 0.409, climbPerKm: 17.1), Is.EqualTo(3));

    [TestCase(0.5, 0, 1)]
    [TestCase(0.51, 0, 2)]
    [TestCase(0, 30.39, 3)]
    public void Each_counts_by_its_weight(double offRoadShare, double climbPerKm, int stars) =>
        Assert.That(Stars.Of(Activity.Run, 0, matchedShare: 1, offRoadShare, climbPerKm), Is.EqualTo(stars));

    //  A ride's cut points 0.0059, 0.0198, 0.0457, 0.0845 (measured 2026-11-10, after T098), by its
    //  score alone: the user has rated too few rides to test more.
    [TestCase(Activity.GravelRide, 0.0058, 1)]
    [TestCase(Activity.GravelRide, 0.0059, 2)]
    [TestCase(Activity.GravelRide, 0.0198, 3)]
    [TestCase(Activity.GravelRide, 0.0457, 4)]
    [TestCase(Activity.GravelRide, 0.0844, 4)]
    [TestCase(Activity.GravelRide, 0.0845, 5)]
    //  No Trail Ride Outings yet: a Gravel Ride's cut points.
    [TestCase(Activity.TrailRide, 0.0457, 4)]
    [TestCase(Activity.TrailRide, 0.0845, 5)]
    public void A_rides_score_gets_the_stars_of_its_fifth(Activity activity, double score, int stars) =>
        Assert.That(Stars.Of(activity, score, matchedShare: 1, offRoadShare: 0, climbPerKm: 0), Is.EqualTo(stars));

    [Test]
    public void A_rides_shape_doesnt_move_its_stars() =>
        Assert.That(Stars.Of(Activity.GravelRide, 0.0058, matchedShare: 1, offRoadShare: 1, climbPerKm: 40), Is.EqualTo(1));

    [Test]
    public void A_ride_without_its_shape_still_has_stars() =>
        Assert.That(Stars.Of(Activity.GravelRide, 0.0845, matchedShare: 1, offRoadShare: null, climbPerKm: null), Is.EqualTo(5));

    //  A Run's stars need its shape (kept since T104): one without it has none.
    [TestCase(null, 10.0)]
    [TestCase(0.5, null)]
    public void A_Run_without_its_shape_gets_none(double? offRoadShare, double? climbPerKm) =>
        Assert.That(Stars.Of(Activity.Run, 0.9, matchedShare: 1, offRoadShare, climbPerKm), Is.Null);

    [TestCase(0.8, 3)]
    [TestCase(0.7999, null)]
    [TestCase(0.0, null)]
    public void A_route_matched_for_less_than_80_percent_of_its_length_gets_none(double matchedShare, int? stars) =>
        Assert.That(Stars.Of(Activity.Run, 0.45, matchedShare, offRoadShare: 0, climbPerKm: 0), Is.EqualTo(stars));

    [Test]
    public void A_score_that_isnt_a_number_gets_none() =>
        Assert.That(Stars.Of(Activity.Run, double.NaN, matchedShare: 1, offRoadShare: 0, climbPerKm: 0), Is.Null);
}
