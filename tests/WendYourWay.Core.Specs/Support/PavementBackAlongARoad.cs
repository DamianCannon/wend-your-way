using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;

namespace WendYourWay.Core.Specs.Support;

/// <summary>
/// Out along a road in two Segments, A=B and B=C, 100 m each; back along a 200 m pavement C-A
/// drawn as one way, which runs beside A=B for 100 m and beside B=C for only 80 m (it bends away
/// for the rest). All of it is Connector. Only those 180 m of the pavement cover ground already
/// covered.
/// </summary>
internal static class PavementBackAlongARoad
{
    private static readonly NodePoint A = new(0, 0), B = new(100, 0), C = new(200, 0);
    private static readonly SegmentKey AB = new(1, 1, 2), BC = new(2, 2, 3), Pavement = new(3, 3, 1);

    public static (NetworkSlice Slice, Loop Loop) Build()
    {
        var slice = NetworkSlice.Assemble(
        [
            new StoredSegment(AB, A, B, 100, GroundKind.Connector, AccessKind.Allowed),
            new StoredSegment(BC, B, C, 100, GroundKind.Connector, AccessKind.Allowed),
            new StoredSegment(Pavement, C, A, 200, GroundKind.Connector, AccessKind.Allowed,
                Beside: [new RoadShare(AB, 100), new RoadShare(BC, 80)]),
        ]);
        int Index(SegmentKey key) => Enumerable.Range(0, slice.Segments.Count).Single(i => slice.Segments[i].Key == key);
        var loop = new Loop(slice.Segments[Index(AB)].From, [],
            [new Traversal(Index(AB), Forward: true), new Traversal(Index(BC), Forward: true), new Traversal(Index(Pavement), Forward: true)]);
        return (slice, loop);
    }
}
