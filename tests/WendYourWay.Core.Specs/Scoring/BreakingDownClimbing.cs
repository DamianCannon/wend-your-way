using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Climbing (PLAN T049): the Breakdown's total ascent, each Segment climbing as it's walked; one
/// walked backwards climbs what it descends going forwards.
/// </summary>
public class BreakingDownClimbing
{
    //  Up a lane 30 m (ascending 30 m forwards), along the top, and down a path stored the other
    //  way (from the bottom up, so walked backwards: its 25 m of ascent is descent, its 5 m of
    //  descent is a rise on the way down).
    private static Breakdown UpAndDown()
    {
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(300, 0), new NodePoint(300, 300)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 300, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 0, AscentM: 30),
            new Segment(new SegmentKey(2, 2, 3), 1, 2, 300, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 1, AscentM: 2, DescentM: 2),
            new Segment(new SegmentKey(3, 1, 3), 0, 2, 430, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 2, AscentM: 25, DescentM: 5),
        ]);
        return Breakdown.Of(slice, new Loop(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, false)]));
    }

    [Test]
    public void A_Loops_Breakdown_gives_its_climbing() =>
        Assert.That(UpAndDown().ClimbM, Is.EqualTo(30.0 + 2 + 5));

    //  R3.5: total ascent and descent, beside the elevation profile (T051).
    [Test]
    public void A_Loops_Breakdown_gives_its_descent() =>
        Assert.That(UpAndDown().DescentM, Is.EqualTo(0.0 + 2 + 25));
}
