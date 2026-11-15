using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Loops;

public class SizingASlice
{
    //  No Loop within the window (up to 11.5 km for 10 km) reaches farther from its start than half
    //  that, and the start may snap up to 500 m from where the user clicked.
    [Test]
    public void A_10_km_Request_needs_the_Network_within_6_25_km() =>
        Assert.That(new Request(new NodePoint(0, 0), DistanceKm: 10).SliceRadiusM, Is.EqualTo(6250));

    //  T068: no Loop offered at 40 or 60 km reached farther than 0.34 of its length from its start
    //  (Great Britain, 2026-11-04): from 30 km, 0.4 of the longest, not half, and the snap.
    [Test]
    public void A_40_km_Request_needs_the_Network_within_18_9_km() =>
        Assert.That(new Request(new NodePoint(0, 0), DistanceKm: 40).SliceRadiusM, Is.EqualTo(18_900).Within(1e-6));

    [Test]
    public void A_100_km_Request_needs_the_Network_within_46_5_km() =>
        Assert.That(new Request(new NodePoint(0, 0), DistanceKm: 100).SliceRadiusM, Is.EqualTo(46_500).Within(1e-6));

    [Test]
    public void Under_30_km_it_s_still_half() =>
        Assert.That(new Request(new NodePoint(0, 0), DistanceKm: 29).SliceRadiusM, Is.EqualTo((29 * 1.15 * 1000 / 2) + 500).Within(1e-6));

    [Test]
    public void A_Request_is_a_Run_unless_it_says_otherwise() =>
        Assert.That(new Request(new NodePoint(0, 0), DistanceKm: 10).Activity, Is.EqualTo(Activity.Run));
}

/// <summary>
/// The window an offered Loop's length must fall in (R1.3): no more than 1 km short of the
/// Distance, and up to 15% over, or 1 km if that's more (user, 2026-10-24).
/// </summary>
public class WindowingALoopsLength
{
    [TestCase(10, 9, 11.5)]
    [TestCase(4, 3, 5)]
    [TestCase(40, 39, 46)]
    public void The_window_is_1_km_short_to_15_percent_or_1_km_over(double distanceKm, double shortestKm, double longestKm)
    {
        var request = new Request(new NodePoint(0, 0), distanceKm);
        Assert.That((request.ShortestKm, request.LongestKm), Is.EqualTo((shortestKm, longestKm)).Using<(double, double)>(
            (a, b) => Math.Abs(a.Item1 - b.Item1) < 1e-9 && Math.Abs(a.Item2 - b.Item2) < 1e-9));
    }
}
