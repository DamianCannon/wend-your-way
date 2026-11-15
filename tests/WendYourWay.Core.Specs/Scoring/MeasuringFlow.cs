using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Flow (CONTEXT.md; PLAN T033): long unbroken stretches of green ground (Off-road or
/// Traffic-free) are worth more than the same distance chopped into scraps. Green in stretches of
/// 2 km or more counts, a road link of 100 m or less doesn't break a stretch, and a Loop's ends
/// join. On the user's rated Loops, green in such stretches separated every very good Loop
/// (3.5–6.1 km) from every good one (0–2.6 km). One character across is 50 m.
/// </summary>
public static class MeasuringFlow
{
    private static string Line(char ground, int characters) => new(ground, characters);

    private static double FlowOf(string map, string walk)
    {
        var network = AsciiNetwork.Parse(map);
        return Flow.Of(network.Slice, network.LoopThrough(walk));
    }

    private static double Score(string map, string walk)
    {
        var network = AsciiNetwork.Parse(map);
        return LoopScorer.Score(network.Slice, network.LoopThrough(walk), ScoringWeights.Default);
    }

    //  2 km of Off-road in one stretch along the top, 2 km of road back along the bottom.
    private static readonly string OneLongStretch = $"""
        S{Line('.', 39)}A
        #{Line(' ', 39)}#
        C{Line('=', 39)}B
        """;

    //  The same ground, but the Off-road comes in 500 m scraps between 500 m of road.
    private static readonly string ChoppedIntoScraps = $"""
        S{Line('.', 9)}D{Line('=', 9)}E{Line('.', 9)}F{Line('=', 9)}A
        #{Line(' ', 39)}#
        C{Line('=', 9)}G{Line('.', 9)}H{Line('=', 9)}I{Line('.', 9)}B
        """;

    [TestFixture]
    public class A_long_unbroken_green_stretch
    {
        [Test]
        public void Then_all_of_it_is_Flow() => Assert.That(FlowOf(OneLongStretch, "SABCS"), Is.EqualTo(2000).Within(1e-9));

        [Test]
        public void Then_it_outscores_the_same_ground_in_scraps() =>
            Assert.That(Score(OneLongStretch, "SABCS"), Is.GreaterThan(Score(ChoppedIntoScraps, "SDEFABIHGCS")));
    }

    //  Out 1.2 km along a towpath and back the same way: 2.4 km on green ground, but only 1.2 km of
    //  it is a stretch; coming back adds nothing (T037 review).
    [TestFixture]
    public class An_out_and_back_along_green_ground
    {
        [Test]
        public void Then_the_way_back_doesn_t_lengthen_the_stretch() =>
            Assert.That(FlowOf($"S{Line('.', 23)}A", "SAS"), Is.Zero);
    }

    [TestFixture]
    public class Green_in_scraps
    {
        [Test]
        public void Then_none_of_it_is_Flow() => Assert.That(FlowOf(ChoppedIntoScraps, "SDEFABIHGCS"), Is.Zero);
    }

    [TestFixture]
    public class A_short_road_link
    {
        //  1,000 m Off-road, a 100 m road crossing, 900 m Off-road: one 2 km stretch.
        [Test]
        public void Then_100_m_of_road_doesn_t_break_a_stretch() =>
            Assert.That(FlowOf($"""
                S{Line('.', 19)}D=E{Line('.', 17)}A
                #{Line(' ', 39)}#
                C{Line('=', 39)}B
                """, "SDEABCS"), Is.EqualTo(2000).Within(1e-9));

        //  150 m of road splits it into 1,000 m and 850 m: neither is long enough.
        [Test]
        public void Then_150_m_of_road_does() =>
            Assert.That(FlowOf($"""
                S{Line('.', 19)}D==E{Line('.', 16)}A
                #{Line(' ', 39)}#
                C{Line('=', 39)}B
                """, "SDEABCS"), Is.Zero);
    }

    //  A route the user ran may cross a path closed to them: it's scored as a Main Road, so it
    //  isn't green ground either (green ground is Off-road networks', the one definition).
    [TestFixture]
    public class A_forbidden_path
    {
        //  1,000 m Off-road, 400 m of forbidden path, 900 m Off-road.
        [Test]
        public void Then_it_breaks_a_stretch() =>
            Assert.That(FlowOf($"""
                S{Line('.', 19)}D{Line('.', 3)}x{Line('.', 3)}E{Line('.', 17)}A
                #{Line(' ', 45)}#
                C{Line('=', 45)}B
                """, "SDEABCS"), Is.Zero);
    }

    [TestFixture]
    public class A_stretch_across_the_start
    {
        //  800 m of Off-road before the start (X.C, then 200 m down C:S) and 1,400 m after it (S.A):
        //  one stretch.
        [Test]
        public void Then_it_counts_whole() =>
            Assert.That(FlowOf($"""
                S{Line('.', 27)}A
                :{Line(' ', 27)}#
                C{Line('.', 11)}X{Line('=', 15)}B
                """, "SABXCS"), Is.EqualTo(2200).Within(1e-9));
    }
}
