using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Scoring;

public static class BreakingDownALoop
{
    [TestFixture]
    public class A_Loop_on_mixed_ground
    {
        //  S=A is 300 m of road, A|C 200 m Traffic-free, then 500 m Off-road back round C.B:S.
        private const string Map = """
            S=====A
            :     |
            B.....C
            """;

        private Breakdown _breakdown = null!;

        [OneTimeSetUp]
        public void Arrange()
        {
            var map = AsciiNetwork.Parse(Map);
            _breakdown = Breakdown.Of(map.Slice, map.LoopThrough("SACBS"));
        }

        [Test]
        public void Then_its_length_is_the_sum_of_its_Segments() =>
            Assert.That(_breakdown.LengthM, Is.EqualTo(1000));

        [Test]
        public void Then_half_of_it_is_Off_road() =>
            Assert.That(_breakdown.OffRoadPercent, Is.EqualTo(50).Within(1e-9));

        [Test]
        public void Then_a_fifth_of_it_is_Traffic_free() =>
            Assert.That(_breakdown.TrafficFreePercent, Is.EqualTo(20).Within(1e-9));

        [Test]
        public void Then_its_Connector_length_is_the_road() =>
            Assert.That(_breakdown.ConnectorLengthM, Is.EqualTo(300));

        [Test]
        public void Then_none_of_it_is_Retrace() =>
            Assert.That(_breakdown.RetraceLengthM, Is.Zero);

        //  700 m of green (A|C then C.B:S) in one stretch: too short for Flow, but its longest.
        [Test]
        public void Then_its_longest_green_stretch_is_given_and_none_of_it_is_Flow() =>
            Assert.That((_breakdown.LongestGreenM, _breakdown.FlowM), Is.EqualTo((700.0, 0.0)));
    }

    [TestFixture]
    public class A_Loop_with_a_short_Main_Road_link
    {
        //  200 m of its 300 m of road is a Main Road (S=m=A), then 100 m of quiet road (A=B).
        [Test]
        public void Then_its_Main_Road_length_is_given() =>
            Assert.That(Breakdown.Of(Map.Slice, Map.LoopThrough("SABCDS")).MainRoadLengthM, Is.EqualTo(200));

        private static readonly AsciiNetwork Map = AsciiNetwork.Parse("""
            S=m=A=B
            :     |
            D.....C
            """, []);
    }

    [TestFixture]
    public class A_Loop_with_a_long_green_stretch
    {
        //  2 km of Off-road along the top, 2.4 km of road back (one character across is 50 m).
        [Test]
        public void Then_its_Flow_is_the_stretch() =>
            Assert.That(Breakdown.Of(Long.Slice, Long.LoopThrough("SABCS")).FlowM, Is.EqualTo(2000).Within(1e-9));

        private static readonly AsciiNetwork Long = AsciiNetwork.Parse($"""
            S{new string('.', 39)}A
            #{new string(' ', 39)}#
            C{new string('=', 39)}B
            """);
    }

    [TestFixture]
    public class A_Loop_back_along_the_other_pavement
    {
        //  Out along S=A, across the road A#B, and back along B=D#S: S=A and D=B are the two
        //  pavements of one 450 m road, stored as the same ground.
        private const string Map = """
            S========A
            #        #
            D========B
            """;

        private Breakdown _breakdown = null!;

        [OneTimeSetUp]
        public void Arrange()
        {
            var map = AsciiNetwork.Parse(Map, "S-A D-B");
            _breakdown = Breakdown.Of(map.Slice, map.LoopThrough("SABDS"));
        }

        [Test]
        public void Then_the_second_pavement_is_Retrace() =>
            Assert.That(_breakdown.RetraceLengthM, Is.EqualTo(450));
    }

    [TestFixture]
    public class A_Loop_back_along_a_pavement_beside_two_stretches_of_road
    {
        [Test]
        public void Then_only_the_metres_beside_the_road_are_Retrace()
        {
            var (slice, loop) = PavementBackAlongARoad.Build();
            Assert.That(Breakdown.Of(slice, loop).RetraceLengthM, Is.EqualTo(180));
        }
    }
}
