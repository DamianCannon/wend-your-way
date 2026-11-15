using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// A route's shape beside its score (PLAN T104): its share off-road and its climbing per km, what
/// the user likes that the score alone misses.
/// </summary>
public class ShapingARoute
{
    [Test]
    public void Its_share_off_road_is_its_off_road_percent_as_a_fraction() =>
        Assert.That(new Breakdown(10_000, 40, 30, 0, 0).OffRoadShare, Is.EqualTo(0.4).Within(1e-12));

    [Test]
    public void Its_climbing_per_km_is_its_climb_over_its_km() =>
        Assert.That(new Breakdown(8_000, 0, 0, 0, 0, ClimbM: 120).ClimbPerKm, Is.EqualTo(15).Within(1e-12));

    [Test]
    public void A_route_of_no_length_climbs_nothing_per_km() =>
        Assert.That(new Breakdown(0, 0, 0, 0, 0, ClimbM: 5).ClimbPerKm, Is.Zero);
}
