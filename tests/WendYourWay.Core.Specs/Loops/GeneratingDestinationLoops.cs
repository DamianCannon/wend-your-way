using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// Destinations (PLAN T041, the user's choice): a Loop goes to the best green place or two within
/// reach and makes the most of it, as Trail Router's best Loops do up one side of the Lea Valley
/// and back down the other, rather than through Anchors on a circle that pull it away.
/// </summary>
public class GeneratingDestinationLoops
{
    //  A grid of streets 100 m apart, 4 km across, and a river running east from x = 0 to 3,000 m
    //  between y = 1,000 and 1,200: a path along each bank, the banks joined only by footbridges at
    //  each end; no street crosses the river.
    private static readonly NetworkSlice RiverInStreets = GridNetwork.Create(junctionsAcross: 41,
        ground: m => OnABank(m) || ABridge(m) ? GroundKind.TrafficFree : GroundKind.Connector,
        forbidden: m => m.X <= 3000 && m.Y is > 1000 and < 1200 && !ABridge(m));

    private static bool OnABank(NodePoint m) => m.X <= 3000 && m.Y is 1000 or 1200;

    private static bool ABridge(NodePoint m) => m.X is 0 or 3000 && m.Y is > 1000 and < 1200;

    //  A 6 km Run from the west end of the south bank.
    [Test]
    public void Then_a_Loop_from_the_end_of_a_river_goes_up_one_bank_and_back_down_the_other()
    {
        var loop = LoopGenerator.Generate(RiverInStreets, new Request(new NodePoint(0, 1000), DistanceKm: 6), ScoringWeights.Default);
        Assert.That(loop, Is.Not.Null);
        double OnBank(double y) => loop!.Traversals.Select(t => RiverInStreets.Segments[t.Segment])
            .Where(s => RiverInStreets.Node(s.From).Y == y && RiverInStreets.Node(s.To).Y == y && RiverInStreets.Node(s.From).X <= 3000 && RiverInStreets.Node(s.To).X <= 3000)
            .Sum(s => s.LengthM);
        Assert.That((OnBank(1000) >= 2500, OnBank(1200) >= 2500), Is.EqualTo((true, true)), $"south bank {OnBank(1000):0} m, north bank {OnBank(1200):0} m");
    }

    //  The same, but the river runs 5 km and footbridges cross it at 0, 2 and 5 km.
    private static bool Bridge(NodePoint m) => m.X is 0 or 2000 or 5000 && m.Y is > 1000 and < 1200;

    private static readonly NetworkSlice LongerRiver = GridNetwork.Create(junctionsAcross: 61,
        ground: m => (m.X <= 5000 && m.Y is 1000 or 1200) || Bridge(m) ? GroundKind.TrafficFree : GroundKind.Connector,
        forbidden: m => m.X <= 5000 && m.Y is > 1000 and < 1200 && !Bridge(m),
        bridgeOverWater: Bridge);

    //  A 4 km Run from the west end of the south bank turns at the bridge 2 km up: Westminster
    //  Bridge, not the streets round Charing Cross (user, 2026-10-24).
    [Test]
    public void Then_a_Loop_up_a_river_crosses_back_at_a_bridge()
    {
        var loop = LoopGenerator.Generate(LongerRiver, new Request(new NodePoint(0, 1000), DistanceKm: 4), ScoringWeights.Default);
        Assert.That(loop, Is.Not.Null);
        var bridges = loop!.Traversals.Select(t => LongerRiver.Segments[t.Segment]).Where(s => s.BridgeOverWater)
            .Select(s => LongerRiver.Node(s.From).X).Distinct().Order();
        Assert.That(bridges, Is.EqualTo(new double[] { 0, 2000 }));
    }
}
