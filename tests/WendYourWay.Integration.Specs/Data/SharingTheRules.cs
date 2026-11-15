using WendYourWay.Core.Loops;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Rules written once (PLAN T121), where they had been written two to five times over: how far
/// apart two places are, what's near enough its start to be a Loop, and the Activity a route's
/// `activity` column holds. No database.
/// </summary>
public class SharingTheRules
{
    //  A thousandth of a degree north is 111.32 m anywhere; east, that times the latitude's cosine.
    [Test]
    public void A_thousandth_of_a_degree_north_is_111_metres() =>
        Assert.That(new LatLon(51.5, -0.1).MetresTo(new LatLon(51.501, -0.1)), Is.EqualTo(111.32).Within(0.01));

    [Test]
    public void East_it_shrinks_with_the_latitude() =>
        Assert.That(new LatLon(51.5, -0.1).MetresTo(new LatLon(51.5, -0.099)), Is.EqualTo(111.32 * Math.Cos(51.5 * Math.PI / 180)).Within(0.01));

    [Test]
    public void It_is_the_same_both_ways()
    {
        var (a, b) = (new LatLon(51.5712, -0.1677), new LatLon(51.5597, -0.1607));
        Assert.That(a.MetresTo(b), Is.EqualTo(b.MetresTo(a)));
    }

    [Test]
    public void Back_within_200_m_of_its_start_is_a_Loop() => Assert.That(PlannedLoop.ClosedWithinM, Is.EqualTo(200));

    //  An Outing's column: a run, walk or hike is a Run; a gravel ride a Gravel Ride; none, a ride on
    //  the road, which informs no Activity of its own, is scored as a Gravel Ride (T097).
    [TestCase("run", Activity.Run)]
    [TestCase("gravel_ride", Activity.GravelRide)]
    [TestCase(null, Activity.GravelRide)]
    public void An_Outings_Activity(string? column, Activity activity) => Assert.That(ActivityColumn.OfOuting(column), Is.EqualTo(activity));

    //  A Saved Route's: none was saved before the stars, and is a Run (T115, user: the three old ones).
    [TestCase("run", Activity.Run)]
    [TestCase("gravel_ride", Activity.GravelRide)]
    [TestCase(null, Activity.Run)]
    public void A_Saved_Routes_Activity(string? column, Activity activity) => Assert.That(ActivityColumn.OfSavedRoute(column), Is.EqualTo(activity));

    //  The column has a Run and a Gravel Ride; a Trail Ride is kept as a Gravel Ride, as before.
    [TestCase(Activity.Run, "run")]
    [TestCase(Activity.GravelRide, "gravel_ride")]
    [TestCase(Activity.TrailRide, "gravel_ride")]
    public void Kept_in_the_column(Activity activity, string column) => Assert.That(ActivityColumn.Of(activity), Is.EqualTo(column));
}
