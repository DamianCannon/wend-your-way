using WendYourWay.Core.Loops;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// A Loop sent through a gate or an Anchor off its natural line goes out to it and back, by the
/// same way or a parallel one: "a weird loop out from Roehampton Gate that makes no sense" (user,
/// 2026-10-23). An excursion that leaves a junction and comes back to it, no more than 40% of the
/// Loop, is cut out (PLAN T042). One character across is 50 m, one line down 100 m.
/// </summary>
public class TrimmingSpurs
{
    private static string Trimmed(string map, string walk)
    {
        var network = AsciiNetwork.Parse(map);
        var trimmed = Spurs.Trim(network.Slice, network.LoopThrough(walk));
        return string.Concat(GeneratingALoop.JunctionsAlong(network.Slice, trimmed).Select(network.NameOf));
    }

    //  A ring S A F C B with a square lobe A D E F hanging off its east side.
    private const string RingWithALobe = """
        S.......A...D
        :       :   :
        :       F...E
        :       :
        B.......C
        """;

    //  Round the lobe from A and back to A, then on round the ring: 800 m of 2,400.
    [Test]
    public void An_excursion_round_a_lobe_is_cut_out() =>
        Assert.That(Trimmed(RingWithALobe, "SAFEDAFCBS"), Is.EqualTo("SAFCBS"));

    //  Out from A to D and back the same way.
    [Test]
    public void Out_to_a_spur_and_back_the_same_way_is_cut_out() =>
        Assert.That(Trimmed(RingWithALobe, "SADAFCBS"), Is.EqualTo("SAFCBS"));

    [Test]
    public void A_ring_is_left_as_it_is() =>
        Assert.That(Trimmed(RingWithALobe, "SAFCBS"), Is.EqualTo("SAFCBS"));

    //  A figure of eight, back through X halfway: each lobe is half the Loop, two Loops in one.
    [Test]
    public void A_lobe_as_long_as_the_rest_is_kept() =>
        Assert.That(Trimmed("""
            A.....S.....C
            :     :     :
            B.....X.....D
            """, "SABXSCDXS"), Is.EqualTo("SABXSCDXS"));
}
