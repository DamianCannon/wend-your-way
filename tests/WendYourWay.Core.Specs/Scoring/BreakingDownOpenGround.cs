using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Open ground (CONTEXT.md; PLAN T039): straight crossings of public open land, which have no OSM
/// way. They count as Off-road, and the Breakdown says how much of a Loop crosses Open ground.
/// </summary>
public class BreakingDownOpenGround
{
    [Test]
    public void A_Segment_with_no_OSM_way_is_Open_ground() =>
        Assert.That((new SegmentKey(0, 7, 9).IsOpenGround, new SegmentKey(4, 7, 9).IsOpenGround), Is.EqualTo((true, false)));

    [Test]
    public void A_Loops_Breakdown_gives_its_length_on_Open_ground()
    {
        //  200 m along a path, 250 m straight across a field, 150 m of path back.
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(100, 0), new NodePoint(100, 100)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 200, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0),
            new Segment(new SegmentKey(0, 2, 3), 1, 2, 250, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 1, Greenery: 1),
            new Segment(new SegmentKey(2, 3, 1), 2, 0, 150, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 2),
        ]);
        var loop = new Loop(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, true)]);
        Assert.That(Breakdown.Of(slice, loop).OpenGroundM, Is.EqualTo(250.0));
    }
}
