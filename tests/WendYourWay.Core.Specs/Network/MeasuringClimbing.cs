using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Network;

/// <summary>
/// Climbing (PLAN T049): a Segment's ascent and descent from the ground's height along it, sampled
/// at both ends and at most every 70 m between.
/// </summary>
public class MeasuringClimbing
{
    // A slope rising 1 m in every 10 m eastwards.
    private static double Slope(NodePoint p) => p.X / 10;

    [Test]
    public void A_Segment_up_an_even_slope_climbs_its_rise() =>
        Assert.That(Climbing.Along([new NodePoint(0, 0), new NodePoint(100, 0)], level: false, Slope), Is.EqualTo((100.0 / 10, 0.0)));

    // A hill 20 m high, its top 140 m along a 280 m Segment that starts and ends at the bottom:
    // sampled every 70 m, the top is a sample.
    private static double Hill(NodePoint p) => 20 - (Math.Abs(p.X - 140) / 7);

    [Test]
    public void A_Segment_over_a_hill_climbs_it_and_comes_down()
    {
        var (ascent, descent) = Climbing.Along([new NodePoint(0, 0), new NodePoint(280, 0)], level: false, Hill);
        Assert.That((ascent, descent), Is.EqualTo((20.0, 20.0)).Using<(double, double)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 1e-9 && Math.Abs(a.Item2 - b.Item2) < 1e-9));
    }

    //  A path up to the hilltop and back down beside itself: the heights follow the line's bends.
    [Test]
    public void A_Segment_that_bends_is_sampled_along_its_bends()
    {
        var (ascent, descent) = Climbing.Along([new NodePoint(0, 0), new NodePoint(140, 0), new NodePoint(0, 10)], level: false, Hill);
        Assert.That((ascent, descent), Is.EqualTo((20.0, 20.0)).Using<(double, double)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 1e-9 && Math.Abs(a.Item2 - b.Item2) < 1e-9));
    }

    //  A bridge or tunnel: the grid is the ground under the deck (a river, a cutting), so only its
    //  ends count, as if level between them.
    [Test]
    public void A_bridge_over_a_valley_neither_climbs_nor_descends() =>
        Assert.That(Climbing.Along([new NodePoint(0, 0), new NodePoint(280, 0)], level: true, p => -Hill(p)), Is.EqualTo((0.0, 0.0)));

    [Test]
    public void A_bridge_climbs_from_its_lower_end_to_its_higher() =>
        Assert.That(Climbing.Along([new NodePoint(0, 0), new NodePoint(280, 0)], level: true, p => Slope(p) - Hill(p)), Is.EqualTo((28.0, 0.0)));
}
