using System.Diagnostics;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A measurement, not a check (T090): five public pairs on London's Network, each planned with no
/// Distance, timed against a Loop as long as its longest from the same start; each route's length
/// and green share (Off-road and Traffic-free, as the car parks list counts it).
/// </summary>
[TestFixture]
[Explicit("A measurement of T090 on London")]
[Category("RealNetwork")]
public class MeasuringPointToPoints
{
    private static readonly (string Name, LatLon From, LatLon To)[] Pairs =
    [
        ("Wimbledon Common → Clapham Common", new(51.4380, -0.2370), new(51.4590, -0.1480)),
        ("Sheen Gate → Hammersmith Bridge", new(51.4636, -0.2654), new(51.4884, -0.2302)),
        ("Finsbury Park → Walthamstow Marshes", new(51.5715, -0.1020), new(51.5700, -0.0500)),
        ("Primrose Hill → Victoria Park", new(51.5394, -0.1608), new(51.5363, -0.0397)),
        ("Crystal Palace Park → Dulwich Park", new(51.4210, -0.0700), new(51.4447, -0.0790)),
    ];

    [Test]
    public async Task Five_pairs_on_London()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(RealNetwork.London);
        var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), ScoringWeights.Default);
        // Warm: the first read of a session is slower.
        await planner.PlanAsync(Pairs[0].From, 5);
        foreach (var (name, from, to) in Pairs)
        {
            // Each timed warm, its area read once already, so neither gains from the other's reads.
            var spread = await planner.PlanPointToPointAsync(from, to);
            var longestKm = spread.Max(r => r.Breakdown.LengthM) / 1000;
            await planner.PlanAsync(from, Math.Max(1, longestKm));
            var clock = Stopwatch.StartNew();
            spread = await planner.PlanPointToPointAsync(from, to);
            var took = clock.Elapsed;
            static double Green(PlannedLoop r) => r.Breakdown.OffRoadPercent + r.Breakdown.TrafficFreePercent;
            clock.Restart();
            await planner.PlanAsync(from, Math.Max(1, longestKm));
            var loopTook = clock.Elapsed;
            await TestContext.Out.WriteLineAsync(
                $"{name}: {spread.Count} offered in {took.TotalSeconds:0.00} s (a {longestKm:0.0} km Loop {loopTook.TotalSeconds:0.00} s); " +
                $"greenest {spread.Max(Green) - Green(spread[0]):+0;-0} points on the first; " +
                string.Join(" | ", spread.Select(r => $"{r.Breakdown.LengthM / 1000:0.0} km {Green(r):0}% green")));
        }
    }
}
