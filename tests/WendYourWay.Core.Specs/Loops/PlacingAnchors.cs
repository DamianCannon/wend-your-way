using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// A Loop is generated through Anchors placed round a circle. At the junction nearest its point on
/// the circle, an Anchor could land among roads, and the Loop then goes out of its way along them
/// to touch it (user, 2026-10-19: a rectangle of side streets). So an Anchor lands on green ground
/// nearby when there is some. On a grid of 100 m blocks where every eighth street each way is a
/// path and the rest are roads.
/// </summary>
public class PlacingAnchors
{
    private static readonly NetworkSlice Grid = GridNetwork.Create(junctionsAcross: 33,
        ground: m => m.X % 800 == 0 || m.Y % 800 == 0 ? GroundKind.TrafficFree : GroundKind.Connector);

    private static AnchorPlacement Placement() => new(Grid, [.. Enumerable.Range(0, Grid.NodeCount)]);

    //  Nearest is the all-road junction at (1000, 1000); the path along y = 800 is 190 m away.
    [Test]
    public void Then_an_Anchor_lands_on_green_ground_nearby() =>
        Assert.That(Grid.Node(Placement().Near(new NodePoint(1010, 990))), Is.EqualTo(new NodePoint(1000, 800)));

    //  A park of paths in the corner (0–800 m each way, about 14 km of them) and a scrap of open
    //  space elsewhere: two 100 m paths meeting at (1600, 1600). Everything else is road.
    private static readonly NetworkSlice ParkAndScrap = GridNetwork.Create(junctionsAcross: 33,
        ground: m => (m.X <= 800 && m.Y <= 800) || m == new NodePoint(1650, 1600) || m == new NodePoint(1600, 1650)
            ? GroundKind.TrafficFree : GroundKind.Connector);

    private static AnchorPlacement PlacementIn(NetworkSlice slice) => new(slice, [.. Enumerable.Range(0, slice.NodeCount)]);

    //  The user saw an Anchor pulled onto a scrap of open space while a wood beside it went half
    //  used (2026-10-19): only green areas with 2 km of paths or more draw an Anchor.
    [Test]
    public void Then_a_scrap_of_green_doesn_t_draw_an_Anchor() =>
        Assert.That(ParkAndScrap.Node(PlacementIn(ParkAndScrap).Near(new NodePoint(1510, 1590))), Is.EqualTo(new NodePoint(1500, 1600)));

    [Test]
    public void Then_a_park_does() =>
        Assert.That(ParkAndScrap.Node(PlacementIn(ParkAndScrap).Near(new NodePoint(1010, 590))), Is.EqualTo(new NodePoint(800, 600)));

    //  All road but for a path along y = 500 from x = 0 to 1,100 m, a gap of road, then more path
    //  to 2,300 m: 1.1 km of path either side, neither enough alone.
    private static NetworkSlice TwoPathsAcrossARoad(double blockM, double gapM) => GridNetwork.Create(
        junctionsAcross: (int)(3000 / blockM) + 1, blockM: blockM,
        ground: m => m.Y == 500 && (m.X < 1100 || m.X > 1100 + gapM) ? GroundKind.TrafficFree : GroundKind.Connector);

    private static NodePoint AnchorFor(NetworkSlice slice) => slice.Node(PlacementIn(slice).Near(new NodePoint(600, 700)));

    //  One 100 m Segment of road, a crossing, joins the paths into one Off-road network of 2.2 km.
    [Test]
    public void Then_paths_either_side_of_a_road_crossing_draw_it_together() =>
        Assert.That(AnchorFor(TwoPathsAcrossARoad(blockM: 100, gapM: 100)), Is.EqualTo(new NodePoint(600, 500)));

    //  On 150 m blocks, 1,050 m of path along y = 450 either side of one 150 m Segment of road.
    [Test]
    public void Then_paths_either_side_of_a_longer_road_don_t()
    {
        var slice = GridNetwork.Create(junctionsAcross: 21, blockM: 150,
            ground: m => m.Y == 450 && (m.X < 1050 || m.X > 1200) ? GroundKind.TrafficFree : GroundKind.Connector);
        Assert.That(slice.Node(PlacementIn(slice).Near(new NodePoint(600, 650))), Is.EqualTo(new NodePoint(600, 600)));
    }

    //  Two 50 m Segments of road don't, though they're no longer than one crossing: bridged along
    //  any chain of streets, pocket squares in a city joined into one network that drew Anchors,
    //  and Trafalgar Square's Loop took 1 km more road (2026-10-23).
    [Test]
    public void Then_paths_either_side_of_a_chain_of_short_roads_don_t() =>
        Assert.That(AnchorFor(TwoPathsAcrossARoad(blockM: 50, gapM: 100)), Is.EqualTo(new NodePoint(600, 700)));

    //  In the middle of a block of roads, about 400 m from any path: too far to go out of the way.
    [Test]
    public void Then_with_no_green_ground_within_reach_it_lands_at_the_nearest_junction() =>
        Assert.That(Grid.Node(Placement().Near(new NodePoint(1205, 1195))), Is.EqualTo(new NodePoint(1200, 1200)));
}
