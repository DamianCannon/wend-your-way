using System.Globalization;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A measurement, not a check (PLAN T050): from starts near hills, on Great Britain's Network
/// (WEND_REAL_DATABASE overrides it), a Loop at each Hills Lean, its climbing per km, green
/// share and time given. With WEND_LOOPS_OUT, each is written there as GPX.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class LeaningOnHillsFromRealStarts
{
    private static readonly (string Name, LatLon Start, double DistanceKm)[] Starts =
    [
        ("kenwood-house", new LatLon(51.5712, -0.1677), 10),
        ("box-hill", new LatLon(51.2510, -0.3127), 10),
        ("box-hill", new LatLon(51.2510, -0.3127), 20),
    ];

    private static readonly double[] Leans = [-1, 0, 1];

    //  WEND_HILLS_WEIGHTS ("fewer cost,more cost,score") tries other weights than the Default's.
    internal static ScoringWeights TriedWeights()
    {
        if (Environment.GetEnvironmentVariable("WEND_HILLS_WEIGHTS") is not { Length: > 0 } tried) return ScoringWeights.Default;
        var parts = tried.Split(',').Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return ScoringWeights.Default with { FewerHillsCostWeight = parts[0], MoreHillsCostWeight = parts[1], HillsScoreWeight = parts[2] };
    }

    [Test]
    [Explicit("A measurement of the Hills Lean")]
    public async Task Generating_at_each_Hills_Lean()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain);
        var weights = TriedWeights();
        var planner = new LoopPlanner(new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource), weights);
        var output = Environment.GetEnvironmentVariable("WEND_LOOPS_OUT");
        foreach (var (name, start, km) in Starts)
        {
            foreach (var lean in Leans)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var planned = await planner.PlanAsync(start, km, new WendYourWay.Core.Loops.Leans(Hills: lean)) is [var best, ..] ? best : null;
                var took = clock.Elapsed;
                if (planned is null)
                {
                    await TestContext.Out.WriteLineAsync($"{name,-14} {km,3:0} km hills {lean,4:+0;-0;0} no Loop");
                    continue;
                }
                var b = planned.Breakdown;
                await TestContext.Out.WriteLineAsync(
                    $"{name,-14} {km,3:0} km hills {lean,4:+0;-0;0} {b.LengthM / 1000:0.0} km, climbing {b.ClimbM:0} m ({b.ClimbM * 1000 / b.LengthM:0.0} m/km), " +
                    $"green {b.OffRoadPercent + b.TrafficFreePercent:0}%, Connector {b.ConnectorLengthM / 1000:0.0} km, Main Road {b.MainRoadLengthM / 1000:0.00} km · {took.TotalSeconds:0.0} s");
                if (string.IsNullOrEmpty(output)) continue;
                await File.WriteAllTextAsync(Path.Combine(output, FormattableString.Invariant($"{name}-{km:0}km-hills{lean:+0;-0;0}.gpx")),
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<gpx version=\"1.1\" creator=\"Wend Your Way\"><trk><name>" +
                    FormattableString.Invariant($"{name} {km:0} km, hills {lean:+0;-0;0}") + "</name><trkseg>" +
                    string.Concat(planned.Line.Select(p => string.Create(CultureInfo.InvariantCulture, $"<trkpt lat=\"{p.Latitude:0.000000}\" lon=\"{p.Longitude:0.000000}\"/>"))) +
                    "</trkseg></trk></gpx>\n");
            }
        }
    }
}
