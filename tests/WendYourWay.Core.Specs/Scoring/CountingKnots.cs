using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Knots (CONTEXT.md; PLAN T042): junctions a Loop comes back to other than while retracing its
/// own steps. They mark what the user called Anchors "pulling the route all over the place": a
/// spur to touch somewhere, a lollipop, a tangle. Going out and back the same way is not a knot.
/// One character across is 50 m.
/// </summary>
public class CountingKnots
{
    private static int KnotsOf(string map, string walk)
    {
        var network = AsciiNetwork.Parse(map);
        return Knots.Of(network.Slice, network.LoopThrough(walk));
    }

    [Test]
    public void A_ring_has_none() =>
        Assert.That(KnotsOf("""
            S.....A
            :     :
            B.....C
            """, "SACBS"), Is.Zero);

    //  Along a canal past a junction M and back the same way: M is passed again, but retracing.
    [Test]
    public void Out_and_back_the_same_way_has_none() =>
        Assert.That(KnotsOf("S.....M.....A", "SMAMS"), Is.Zero);

    //  Round the ring with a spur out to D and back: the ring is left at A and come back to.
    [Test]
    public void A_spur_off_the_ring_is_one() =>
        Assert.That(KnotsOf("""
            S.....A.....D
            :     :
            B.....C
            """, "SADACBS"), Is.EqualTo(1));

    //  A figure of eight, back through the start halfway round: S and X are each come back to.
    [Test]
    public void A_figure_of_eight_through_the_start_is_two() =>
        Assert.That(KnotsOf("""
            A.....S.....C
            :     :     :
            B.....X.....D
            """, "SABXSCDXS"), Is.EqualTo(2));

    //  Out along a stem to X, round a loop and back down the stem: X is come back to.
    [Test]
    public void A_lollipop_is_one() =>
        Assert.That(KnotsOf("""
            S.....X.....A
                  :     :
                  Y.....C
            """, "SXACYXS"), Is.EqualTo(1));
}
