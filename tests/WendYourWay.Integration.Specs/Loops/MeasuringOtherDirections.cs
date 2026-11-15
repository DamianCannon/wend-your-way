using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A measurement, not a check (PLAN T096, STORY-044): from five public starts on London, how many
/// Loops each Request offers on the usual bar, how many it adds in other directions, and how those
/// score against the first and how much more Main Road they take.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class MeasuringOtherDirections
{
    private static readonly (string Name, LatLon Start)[] Starts =
    [
        ("kenwood-house", new LatLon(51.5712, -0.1677)),
        ("pembroke-lodge", new LatLon(51.4426, -0.3006)),
        ("greenwich-park", new LatLon(51.4769, 0.0005)),
        ("springfield-park", new LatLon(51.5695, -0.0570)),
        ("wanstead-flats", new LatLon(51.5530, 0.0270)),
    ];

    private static readonly (Activity Activity, double Km)[] Requests =
    [
        (Activity.Run, 5), (Activity.Run, 10), (Activity.Run, 15), (Activity.Run, 21),
        (Activity.GravelRide, 30), (Activity.GravelRide, 50),
    ];

    [Test]
    [Explicit("A measurement for STORY-044")]
    public async Task Loops_in_other_directions()
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.London);
        var (slices, geometry) = (new PostgisNetworkSlices(dataSource), new MapGeometry(dataSource));
        var (requests, reachingFive, added) = (0, 0, 0);
        foreach (var (activity, km) in Requests)
        {
            foreach (var (name, at) in Starts)
            {
                var request = new Request(await geometry.ToGridAsync(at), km, Activity: activity);
                var slice = await slices.AroundAsync(request.Start, request.SliceRadiusM, activity);
                var offered = LoopGenerator.Offer(slice, request, ScoringWeights.Default);
                var weights = ScoringWeights.Default.For(activity);
                var first = offered.Count > 0 ? LoopScorer.Score(slice, offered[0], weights) : 0;
                var firstMainRoadM = offered.Count > 0 ? Breakdown.Of(slice, offered[0]).MainRoadLengthM : 0;
                var others = offered.Skip(offered.OnTheBar).Select(l => FormattableString.Invariant(
                    $"{LoopScorer.Score(slice, l, weights) / first:0.00}/{(Breakdown.Of(slice, l).MainRoadLengthM - firstMainRoadM) / 1000:+0.0;-0.0}"));
                (requests, reachingFive, added) = (requests + 1, reachingFive + (offered.Count == LoopGenerator.Offered ? 1 : 0), added + offered.Count - offered.OnTheBar);
                await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                    $"DIR {name,-16} {activity,-10} {km,3:0} km: on the bar {offered.OnTheBar}, offered {offered.Count}; added (score share/Main Road km more): ") + string.Join(" ", others));
            }
        }
        await TestContext.Out.WriteLineAsync($"DIR {requests} Requests, {reachingFive} offering five, {added} Loops added in other directions");
    }
}
