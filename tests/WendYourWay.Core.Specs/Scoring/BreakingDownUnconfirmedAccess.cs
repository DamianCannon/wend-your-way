using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Unconfirmed Access (CONTEXT.md; SPEC R9.1; PLAN T068): a Gravel Ride may use a way whose access
/// the Network can't tell, and its Breakdown says how much.
/// </summary>
public class BreakingDownUnconfirmedAccess
{
    [Test]
    public void A_Loops_Breakdown_gives_its_length_on_Unconfirmed_Access()
    {
        //  200 m of bridleway, 250 m of a path the tags can't tell, 150 m of lane back.
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(100, 0), new NodePoint(100, 100)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 200, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0),
            new Segment(new SegmentKey(2, 2, 3), 1, 2, 250, GroundKind.OffRoad, AccessKind.Unconfirmed, SameGroundGroup: 1),
            new Segment(new SegmentKey(3, 3, 1), 2, 0, 150, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 2),
        ]);
        var loop = new Loop(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, true)]);
        Assert.That(Breakdown.Of(slice, loop).UnconfirmedM, Is.EqualTo(250.0));
    }
}
