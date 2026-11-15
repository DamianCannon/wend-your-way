using System.Diagnostics;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Routing;

/// <summary>
/// ADR 0006's central claim: A* over a London-sized slice takes milliseconds per Path. Measured
/// on ~200,000 Segments with a realistic spread of costs (Off-road ×1, Traffic-free ×1.5, roads
/// ×3). A wide spread weakens A*'s straight-line estimate, so this is the honest case, not the
/// flattering uniform-cost one. Excluded from the fast run: dotnet test --filter "Category!=Performance".
/// </summary>
[TestFixture]
[Category("Performance")]
public class PathFinderPerformance
{
    private const int Side = 317;            // 317 × 317 junctions ≈ 100k nodes, ≈ 200k Segments
    private const double Block = 80;         // metres between junctions, about a London block

    private NetworkSlice _slice = null!;
    private TimeSpan _build;

    [OneTimeSetUp]
    public void Arrange()
    {
        var random = new Random(2026);
        var nodes = new NodePoint[Side * Side];
        for (var r = 0; r < Side; r++)
            for (var c = 0; c < Side; c++)
                nodes[(r * Side) + c] = new NodePoint(
                    (c * Block) + ((random.NextDouble() - 0.5) * Block * 0.4),
                    (r * Block) + ((random.NextDouble() - 0.5) * Block * 0.4));

        var segments = new List<Segment>(2 * Side * Side);
        void Join(int a, int b)
        {
            var roll = random.NextDouble();
            var ground = roll < 0.3 ? GroundKind.OffRoad : roll < 0.6 ? GroundKind.TrafficFree : GroundKind.Connector;
            segments.Add(new Segment(
                new SegmentKey(segments.Count + 1, a + 1, b + 1), a, b,
                LengthM: nodes[a].DistanceTo(nodes[b]) * (1 + (random.NextDouble() * 0.3)),
                ground,
                random.NextDouble() < 0.03 ? AccessKind.Forbidden : AccessKind.Allowed,
                SameGroundGroup: segments.Count));
        }
        for (var r = 0; r < Side; r++)
            for (var c = 0; c < Side; c++)
            {
                if (c + 1 < Side) Join((r * Side) + c, (r * Side) + c + 1);
                if (r + 1 < Side) Join((r * Side) + c, ((r + 1) * Side) + c);
            }

        var clock = Stopwatch.StartNew();
        _slice = new NetworkSlice(nodes, segments);
        _build = clock.Elapsed;
    }

    [Test]
    public void Then_Paths_of_two_to_five_kilometres_take_milliseconds()
    {
        var cost = new RunCostModel(ScoringWeights.Default);
        var random = new Random(7);
        var timings = new List<double>();
        var lengths = new List<double>();

        // Warm up the JIT so the first measurement isn't compilation.
        PathFinder.Find(_slice, PathEnd.AtNode(0), PathEnd.AtNode(Side + 1), cost);

        while (timings.Count < 25)
        {
            var from = random.Next(_slice.NodeCount);
            var to = random.Next(_slice.NodeCount);
            var crow = _slice.Node(from).DistanceTo(_slice.Node(to));
            if (crow is < 2000 or > 5000) continue;

            var clock = Stopwatch.StartNew();
            var path = PathFinder.Find(_slice, PathEnd.AtNode(from), PathEnd.AtNode(to), cost);
            clock.Stop();
            if (path is null) continue;
            timings.Add(clock.Elapsed.TotalMilliseconds);
            lengths.Add(path.LengthM);
        }

        timings.Sort();
        var median = timings[timings.Count / 2];
        var worst = timings[^1];
        TestContext.Out.WriteLine(
            $"slice: {_slice.NodeCount:N0} junctions, {_slice.Segments.Count:N0} Segments, built in {_build.TotalMilliseconds:N0} ms");
        TestContext.Out.WriteLine(
            $"{timings.Count} Paths of {lengths.Min() / 1000:0.0}–{lengths.Max() / 1000:0.0} km: median {median:0.0} ms, worst {worst:0.0} ms");

        // Generation finds a few hundred Paths per Request within ~25 s, so even 50 ms a Path
        // leaves plenty of room; the bound is set well above the measured median.
        Assert.That(median, Is.LessThan(50), "median milliseconds per Path");
    }
}
