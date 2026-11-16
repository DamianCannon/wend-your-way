using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Network;

/// <summary>
/// A route through junctions split into corners, as the map has it (#149, T145c): drawn, saved and
/// reopened by its OSM ways alone. Crossing links have no way, so they're left out; a carriageway's
/// two sides are one way, so it's saved once; and reopened, each way's side is the one that joins
/// up, and the crossings between are put back.
/// </summary>
public static class PuttingCornersOnTheMap
{
    private static (NetworkSlice Slice, IReadOnlyList<Traversal> Path, AsciiNetwork Drawn) Routed(string map)
    {
        var drawn = AsciiNetwork.Parse(map);
        var slice = Corners.Split(drawn.Slice);
        int At(char name) => Enumerable.Range(0, slice.NodeCount).First(n => slice.Node(n) == drawn.Slice.Node(drawn.Node(name)));
        var path = PathFinder.Find(slice, PathEnd.AtNode(At('S')), PathEnd.AtNode(At('T')), new RunCostModel(ScoringWeights.Default))
            ?? throw new AssertionException("No Path from S to T.");
        return (slice, path.Traversals, drawn);
    }

    // Across at one junction, and along one side from one junction to the next.
    private const string Across = """
              S
              #
        W==c==J==c==E
              #
              T
        """;

    private const string Along = """
           S     T
           #     #
        Wc=J==c==K=cE
        """;

    [TestFixture]
    public class Given_a_route_across_a_Main_Road
    {
        [Test]
        public void Then_on_the_map_it_s_its_ways_alone()
        {
            var (slice, path, drawn) = Routed(Across);
            Assert.That(Corners.OnTheMap(slice, path).Select(t => t.Key),
                Is.EqualTo(new[] { drawn.Slice.Segments[drawn.Segment("S-J")].Key, drawn.Slice.Segments[drawn.Segment("J-T")].Key }));
        }

        [Test]
        public void Then_reopened_it_crosses_where_it_did()
        {
            var (slice, path, _) = Routed(Across);
            Assert.That(Corners.Rejoin(slice, [.. Corners.OnTheMap(slice, path)]), Is.EqualTo(path));
        }

        [Test]
        public void Then_its_length_leaves_the_crossing_out_but_counts_it()
        {
            var (slice, path, drawn) = Routed(Across);
            var breakdown = Breakdown.Of(slice, new Loop(path[0].Forward ? slice.Segments[path[0].Segment].From : slice.Segments[path[0].Segment].To, [], path));
            Assert.That((breakdown.LengthM, breakdown.MainRoadCrossings, breakdown.UnmarkedMainRoadCrossings),
                Is.EqualTo((drawn.Slice.Segments[drawn.Segment("S-J")].LengthM + drawn.Slice.Segments[drawn.Segment("J-T")].LengthM, 1, 1)));
        }
    }

    [TestFixture]
    public class Given_a_route_along_one_side_of_a_Main_Road
    {
        [Test]
        public void Then_reopened_it_keeps_to_that_side()
        {
            var (slice, path, _) = Routed(Along);
            Assert.That(Corners.Rejoin(slice, [.. Corners.OnTheMap(slice, path)]), Is.EqualTo(path));
        }
    }

    [TestFixture]
    public class Given_a_way_the_slice_hasn_t_got
    {
        [Test]
        public void Then_it_can_t_be_reopened()
        {
            var (slice, path, _) = Routed(Across);
            Assert.That(Corners.Rejoin(slice, [.. Corners.OnTheMap(slice, path), (new SegmentKey(999_999, 1, 2), true)]), Is.Null);
        }
    }

    [TestFixture]
    public class Given_a_junction_split_into_corners
    {
        [Test]
        public void Then_its_corners_are_found_from_any_of_them()
        {
            var drawn = AsciiNetwork.Parse(Across);
            var slice = Corners.Split(drawn.Slice);
            var at = drawn.Slice.Node(drawn.Node('J'));
            var corners = Enumerable.Range(0, slice.NodeCount).Where(n => slice.Node(n) == at).ToArray();
            Assert.That(corners.Select(c => Corners.Of(slice, c).Order()), Has.All.EqualTo(corners));
        }
    }
}
