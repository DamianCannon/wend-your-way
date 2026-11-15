using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Support;

/// <summary>The ASCII map helper must build exactly the network that is drawn: no more, no less.</summary>
public static class DrawingAsciiNetworks
{
    [TestFixture]
    public class A_map_with_several_forbidden_lines
    {
        //  Two forbidden Segments, and a vertical line meeting the middle of one of them.
        private const string Map = """
            S--x--A--x--T
            |           |
            B-----------C
            """;

        private AsciiNetwork _map = null!;

        [OneTimeSetUp]
        public void Arrange() => _map = AsciiNetwork.Parse(Map);

        [Test]
        public void Then_it_has_exactly_the_Segments_drawn() =>
            Assert.That(_map.Slice.Segments, Has.Count.EqualTo(5));   // S-A, A-T, S-B, T-C, B-C

        [Test]
        public void Then_both_marked_lines_are_forbidden() =>
            Assert.That(
                new[] { _map.Segment("S-A"), _map.Segment("A-T") }.Select(s => _map.Slice.Segments[s].Access),
                Is.All.EqualTo(AccessKind.Forbidden));

        [Test]
        public void Then_x_is_never_a_junction() =>
            Assert.That(() => _map.Node('x'), Throws.ArgumentException);
    }
}
