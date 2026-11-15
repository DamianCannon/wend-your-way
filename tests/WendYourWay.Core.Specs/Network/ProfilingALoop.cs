using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Network;

/// <summary>
/// The elevation profile (PLAN T051, R3.5): the ground's height along a Loop's line by distance
/// from its start, sampled at its ends and at most every 50 m between, for the pager card.
/// </summary>
public class ProfilingALoop
{
    // A slope rising 1 m in every 10 m eastwards.
    private static double Slope(NodePoint p) => p.X / 10;

    [Test]
    public void A_line_up_a_slope_starts_at_its_foot_and_ends_at_its_top()
    {
        var profile = ElevationProfile.Along([new NodePoint(0, 0), new NodePoint(100, 0)], Slope);
        Assert.That((profile[0].DistanceM, profile[0].HeightM, profile[^1].DistanceM, profile[^1].HeightM), Is.EqualTo((0.0, 0.0, 100.0, 10.0)));
    }

    //  120 m in three even steps of 40 m, none longer than 50 m: a hill in between shows.
    [Test]
    public void A_long_stretch_is_sampled_at_most_every_50_m() =>
        Assert.That(ElevationProfile.Along([new NodePoint(0, 0), new NodePoint(120, 0)], Slope).Select(p => (p.DistanceM, p.HeightM)),
            Is.EqualTo(new[] { (0.0, 0.0), (40.0, 4.0), (80.0, 8.0), (120.0, 12.0) }));

    //  Round a corner, the distance runs on along the line, not straight back to the start.
    [Test]
    public void Round_a_corner_the_distance_follows_the_line() =>
        Assert.That(ElevationProfile.Along([new NodePoint(0, 0), new NodePoint(30, 0), new NodePoint(30, 40)], Slope)[^1].DistanceM, Is.EqualTo(70.0).Within(1e-9));
}
