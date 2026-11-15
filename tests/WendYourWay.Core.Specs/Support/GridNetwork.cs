using WendYourWay.Core.Network;

namespace WendYourWay.Core.Specs.Support;

/// <summary>
/// A square grid of streets and paths, big enough to generate real Loops on: junctions every
/// <c>block</c> metres, a Segment along each side of each block. Each Segment's ground and
/// access come from functions of its midpoint, so a spec can say "the western half is Off-road".
/// <c>winding</c> makes every Segment that many times longer than the straight line, as paths
/// through woods are.
/// </summary>
internal static class GridNetwork
{
    public static NetworkSlice Create(
        int junctionsAcross,
        double blockM = 100,
        double winding = 1,
        Func<NodePoint, GroundKind>? ground = null,
        Func<NodePoint, bool>? forbidden = null,
        Func<NodePoint, bool>? bridgeOverWater = null,
        IEnumerable<StoredSegment>? extra = null,
        Func<NodePoint, double>? climbing = null,
        Func<NodePoint, double>? greenery = null)
    {
        var stored = new List<StoredSegment>();
        long NodeId(int column, int row) => (row * junctionsAcross) + column + 1;
        NodePoint At(int column, int row) => new(column * blockM, row * blockM);

        void Add(int c1, int r1, int c2, int r2)
        {
            var from = At(c1, r1);
            var to = At(c2, r2);
            var middle = new NodePoint((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            stored.Add(new StoredSegment(
                new SegmentKey(stored.Count + 1, NodeId(c1, r1), NodeId(c2, r2)),
                from, to, blockM * winding,
                ground?.Invoke(middle) ?? GroundKind.TrafficFree,
                forbidden?.Invoke(middle) == true ? AccessKind.Forbidden : AccessKind.Allowed,
                BridgeOverWater: bridgeOverWater?.Invoke(middle) == true,
                AscentM: climbing?.Invoke(middle) ?? 0,
                DescentM: climbing?.Invoke(middle) ?? 0,
                Greenery: greenery?.Invoke(middle) ?? 0));
        }

        for (var row = 0; row < junctionsAcross; row++)
        {
            for (var column = 0; column < junctionsAcross; column++)
            {
                if (column + 1 < junctionsAcross) Add(column, row, column + 1, row);
                if (row + 1 < junctionsAcross) Add(column, row, column, row + 1);
            }
        }
        if (extra is not null) stored.AddRange(extra);
        return NetworkSlice.Assemble(stored);
    }
}
