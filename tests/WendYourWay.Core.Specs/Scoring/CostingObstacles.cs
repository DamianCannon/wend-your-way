using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// Obstacles (CONTEXT.md; PLAN T069): gates, stiles and the like slow a Gravel Ride, so it pays
/// for them in Off-road metres, as for a Crossing; a Run doesn't. Never forbidden: worth paying
/// when there's no good way round. `s` on a line marks a stile.
/// </summary>
public class CostingObstacles
{
    private static string RouteFrom(string map, Activity activity)
    {
        var network = AsciiNetwork.Parse(map);
        var path = PathFinder.Find(network.Slice, PathEnd.AtNode(network.Node('S')), PathEnd.AtNode(network.Node('T')),
            new RunCostModel(ScoringWeights.Default.For(activity)));
        var junctions = new List<char> { 'S' };
        foreach (var traversal in path!.Traversals)
        {
            var segment = network.Slice.Segments[traversal.Segment];
            junctions.Add(network.NameOf(traversal.Forward ? segment.To : segment.From));
        }
        return new string([.. junctions]);
    }

    //  500 m of Off-road over a stile (S..s..A, A:T), or 500 m of Traffic-free round without
    //  one (S|B, B--T), a little dearer: a stile costs a Gravel Ride as much as 300 m more
    //  Off-road, so it goes round; a Run takes the cheaper ground.
    private const string StileOrRound = """
        S..s..A
        |     :
        B-----T
        """;

    [Test]
    public void A_Gravel_Ride_goes_round_a_stile() =>
        Assert.That(RouteFrom(StileOrRound, Activity.GravelRide), Is.EqualTo("SBT"));

    [Test]
    public void A_Run_goes_over_it() =>
        Assert.That(RouteFrom(StileOrRound, Activity.Run), Is.EqualTo("SAT"));

    //  300 m over the stile, or 700 m of Traffic-free round: the stile is worth crossing.
    [Test]
    public void But_not_the_long_way_round() =>
        Assert.That(RouteFrom("""
            S..s..T
            |     |
            B-----C
            """, Activity.GravelRide), Is.EqualTo("ST"));

    //  A gate at a junction is shared half and half by the Segments meeting there, so a Loop
    //  passing through it pays for it once.
    [Test]
    public void Half_an_Obstacle_costs_half()
    {
        var weights = ScoringWeights.Default.For(Activity.GravelRide);
        Segment With(double share) => new(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0)
        {
            Obstacles = [new ObstacleShare(ObstacleKind.Gate, share)],
        };
        var bare = new RunCostModel(weights).CostOf(With(0), forward: true);
        Assert.That(
            new RunCostModel(weights).CostOf(With(0.5), forward: true) - bare,
            Is.EqualTo(0.5 * weights.OffRoadPerMetre * ObstacleCosts.GravelRide.Of(ObstacleKind.Gate)).Within(1e-9));
    }

    [Test]
    public void A_Run_pays_nothing_for_any_Obstacle() =>
        Assert.That(ScoringWeights.Default.For(Activity.Run).Obstacles, Is.Null);

    //  Through the generator: the western half of a 2 km grid is Off-road, a stile on every
    //  block; the eastern half Traffic-free. A Run takes the cheaper Off-road, stiles and all; a
    //  Gravel Ride's Loops are costed for a Gravel Ride, and go east.
    private static int StilesOn(Activity activity)
    {
        var grid = GridNetwork.Create(junctionsAcross: 21, ground: m => m.X < 1000 ? GroundKind.OffRoad : GroundKind.TrafficFree);
        var stiled = new NetworkSlice(
            [.. Enumerable.Range(0, grid.NodeCount).Select(grid.Node)],
            [.. grid.Segments.Select(s => (grid.Node(s.From).X + grid.Node(s.To).X) / 2 < 1000
                ? s with { Obstacles = [new ObstacleShare(ObstacleKind.Stile, 1)] } : s)]);
        var loop = LoopGenerator.Generate(stiled, new Request(new NodePoint(1000, 1000), DistanceKm: 4, Activity: activity), ScoringWeights.Default);
        return Breakdown.Of(stiled, loop!).Obstacles?.Sum(o => o.Count) ?? 0;
    }

    [Test]
    public void A_Run_s_Loop_crosses_the_stiles() =>
        Assert.That(StilesOn(Activity.Run), Is.GreaterThan(0));

    [Test]
    public void A_Gravel_Ride_s_Loop_keeps_clear_of_them() =>
        Assert.That(StilesOn(Activity.GravelRide), Is.Zero);

    //  On the Loop card, each kind and how many: a gate on a junction passed through is one gate.
    [Test]
    public void The_Breakdown_counts_them_by_kind()
    {
        var network = AsciiNetwork.Parse("""
            S..s..A
            :     :
            C.....B
            """);
        var slice = network.Slice;
        var halfGate = new ObstacleShare(ObstacleKind.Gate, 0.5);
        var gated = new NetworkSlice(
            [.. Enumerable.Range(0, slice.NodeCount).Select(slice.Node)],
            [.. slice.Segments.Select(s => s == slice.Segments[network.Segment("A-B")] || s == slice.Segments[network.Segment("B-C")]
                ? s with { Obstacles = [halfGate] } : s)]);
        var breakdown = Breakdown.Of(gated, network.LoopThrough("SABCS"));
        Assert.That(breakdown.Obstacles, Is.EquivalentTo(new[] { new ObstacleCount(ObstacleKind.Gate, 1), new ObstacleCount(ObstacleKind.Stile, 1) }));
    }
}
