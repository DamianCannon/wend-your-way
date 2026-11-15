using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Routing;

public static class FindingTheCheapestPath
{
    [TestFixture]
    public class An_off_road_detour_beats_a_shorter_road
    {
        //  The road S=T is 500 m; the Off-road way round S:A.B:T is 1,300 m.
        //  At the default weights a metre of road costs three metres of Off-road,
        //  so the road costs the same as 1,500 m Off-road and the detour wins.
        private const string Map = """
            S=========T
            :         :
            :         :
            :         :
            A.........B
            """;

        private AsciiNetwork _map = null!;
        private SegmentPath? _path;

        [OneTimeSetUp]
        public void Arrange()
        {
            _map = AsciiNetwork.Parse(Map);
            _path = PathFinder.Find(
                _map.Slice,
                PathEnd.AtNode(_map.Node('S')),
                PathEnd.AtNode(_map.Node('T')),
                new RunCostModel(ScoringWeights.Default));
        }

        [Test]
        public void Then_the_Path_goes_the_Off_road_way_round() =>
            Assert.That(
                _path!.Traversals.Select(t => t.Segment),
                Is.EqualTo(new[] { _map.Segment("S-A"), _map.Segment("A-B"), _map.Segment("B-T") }));

        // Lines are drawn top-to-bottom and left-to-right, so S→A and A→B run with the
        // Segment's direction and B→T runs against it.
        [Test]
        public void Then_each_Traversal_records_its_direction() =>
            Assert.That(_path!.Traversals.Select(t => t.Forward), Is.EqualTo(new[] { true, true, false }));

        [Test]
        public void Then_its_length_is_the_sum_of_its_Segments() =>
            Assert.That(_path!.LengthM, Is.EqualTo(1300));
    }

    [TestFixture]
    public class A_forbidden_shortcut_is_never_used
    {
        //  S-x-T is the short way but a Run may not use it.
        private const string Map = """
            S--x--T
            |     |
            A-----B
            """;

        private AsciiNetwork _map = null!;
        private SegmentPath? _path;

        [OneTimeSetUp]
        public void Arrange()
        {
            _map = AsciiNetwork.Parse(Map);
            _path = PathFinder.Find(_map.Slice, PathEnd.AtNode(_map.Node('S')), PathEnd.AtNode(_map.Node('T')),
                new RunCostModel(ScoringWeights.Default));
        }

        [Test]
        public void Then_the_Path_goes_round_it() =>
            Assert.That(_path!.Traversals.Select(t => t.Segment), Does.Not.Contain(_map.Segment("S-T")));
    }

    [TestFixture]
    public class When_only_forbidden_ground_leads_there
    {
        private const string Map = """
            S-----A--x--T
            """;

        private SegmentPath? _path;

        [OneTimeSetUp]
        public void Arrange()
        {
            var map = AsciiNetwork.Parse(Map);
            _path = PathFinder.Find(map.Slice, PathEnd.AtNode(map.Node('S')), PathEnd.AtNode(map.Node('T')),
                new RunCostModel(ScoringWeights.Default));
        }

        [Test]
        public void Then_there_is_no_Path() => Assert.That(_path, Is.Null);
    }

    [TestFixture]
    public class Ground_a_Loop_has_already_used_is_avoided
    {
        //  P-Q and S-T are the two pavements of one road: one same-ground group.
        //  The Loop has already come along P-Q, so S-T would be a Retrace;
        //  the way round S|A-B|T is longer but fresh.
        private const string Map = """
            P-----Q
            S-----T
            |     |
            A-----B
            """;

        private AsciiNetwork _map = null!;
        private SegmentPath? _path;

        [OneTimeSetUp]
        public void Arrange()
        {
            _map = AsciiNetwork.Parse(Map, sameGround: "P-Q S-T");
            var used = new HashSet<int> { _map.Slice.Segments[_map.Segment("P-Q")].SameGround.Single().Group };
            _path = PathFinder.Find(_map.Slice, PathEnd.AtNode(_map.Node('S')), PathEnd.AtNode(_map.Node('T')),
                new RunCostModel(ScoringWeights.Default, usedGround: used));
        }

        [Test]
        public void Then_the_other_pavement_is_avoided_too() =>
            Assert.That(_path!.Traversals.Select(t => t.Segment), Does.Not.Contain(_map.Segment("S-T")));
    }

    [TestFixture]
    public class When_used_ground_is_the_only_way
    {
        private const string Map = """
            S-----A-----T
            """;

        private AsciiNetwork _map = null!;
        private SegmentPath? _path;

        [OneTimeSetUp]
        public void Arrange()
        {
            _map = AsciiNetwork.Parse(Map);
            var used = new HashSet<int> { _map.Slice.Segments[_map.Segment("S-A")].SameGround.Single().Group };
            _path = PathFinder.Find(_map.Slice, PathEnd.AtNode(_map.Node('S')), PathEnd.AtNode(_map.Node('T')),
                new RunCostModel(ScoringWeights.Default, usedGround: used));
        }

        [Test]
        public void Then_it_is_used_anyway() =>
            Assert.That(_path!.Traversals.Select(t => t.Segment), Does.Contain(_map.Segment("S-A")));
    }
}
