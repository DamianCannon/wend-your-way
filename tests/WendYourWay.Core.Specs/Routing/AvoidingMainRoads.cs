using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Routing;

/// <summary>
/// A Main Road (R2.1; CONTEXT.md) is used only for a short link: the user "never" wants to run
/// along one (2026-10-19), and a Loop had gone the main-road way to reach a long green stretch,
/// because every road cost the same. `m` on a line marks a Main Road.
/// </summary>
public static class AvoidingMainRoads
{
    private static string RouteFrom(string map)
    {
        var network = AsciiNetwork.Parse(map);
        var path = PathFinder.Find(network.Slice, PathEnd.AtNode(network.Node('S')), PathEnd.AtNode(network.Node('T')),
            new RunCostModel(ScoringWeights.Default));
        var junctions = new List<char> { 'S' };
        var at = network.Node('S');
        foreach (var traversal in path!.Traversals)
        {
            var segment = network.Slice.Segments[traversal.Segment];
            at = traversal.Forward ? segment.To : segment.From;
            junctions.Add(map.First(ch => char.IsAsciiLetterUpper(ch) && network.Node(ch) == at));
        }
        return new string([.. junctions]);
    }

    [TestFixture]
    public class Given_a_main_road_or_quiet_streets_round
    {
        //  300 m along a main road (S=m=T), or 700 m round by quiet streets (S#A, A=B, B#T): the
        //  quiet way, at more than twice the length.
        [Test]
        public void Then_it_goes_the_quiet_way() =>
            Assert.That(RouteFrom("""
                S==m===T
                #      #
                A======B
                """), Is.EqualTo("SABT"));
    }

    [TestFixture]
    public class Given_a_main_road_or_a_long_way_round
    {
        //  200 m along a main road (S=m=T), or 1.2 km round by quiet streets: a short link is
        //  still taken.
        [Test]
        public void Then_it_takes_the_short_link() =>
            Assert.That(RouteFrom("""
                S=m=T
                #   #
                #   #
                #   #
                #   #
                A===B
                """), Is.EqualTo("ST"));
    }
}
