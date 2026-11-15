using System.Diagnostics;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The Great Britain spike (PLAN T018): how long a Request's slice takes to load from the Great
/// Britain Network, how much memory it takes, and how long a Loop takes over it, for a 10 km Run
/// and a 40 km Ride (whose slice a Gravel Ride will need), in London and in the country. London's
/// starts are timed against the London Network the app used before (T031) too.
/// Measurements, not checks: run explicitly, and read the output.
/// </summary>
[TestFixture]
[Explicit("A measurement on the Great Britain build (T018)")]
[Category("RealNetwork")]
[NonParallelizable]
public class LoadingSlicesOfGreatBritain
{
    // Public landmarks: London's densest ground, and the Surrey Hills' gravel country.
    private static readonly (string Name, double Latitude, double Longitude) KenwoodHouse = ("Kenwood House", 51.5712, -0.1677);
    private static readonly (string Name, double Latitude, double Longitude) BoxHill = ("Box Hill", 51.2557, -0.3138);

    private static IEnumerable<TestCaseData> Requests()
    {
        foreach (var database in new[] { null, RealNetwork.London })
        {
            yield return new TestCaseData(database, KenwoodHouse.Name, KenwoodHouse.Latitude, KenwoodHouse.Longitude, 10.0);
            yield return new TestCaseData(database, KenwoodHouse.Name, KenwoodHouse.Latitude, KenwoodHouse.Longitude, 40.0);
        }
        yield return new TestCaseData(null, BoxHill.Name, BoxHill.Latitude, BoxHill.Longitude, 10.0);
        yield return new TestCaseData(null, BoxHill.Name, BoxHill.Latitude, BoxHill.Longitude, 40.0);
    }

    [TestCaseSource(nameof(Requests))]
    public async Task A_slice_and_a_Loop(string? database, string start, double latitude, double longitude, double distanceKm)
    {
        await using var dataSource = await RealNetwork.OpenOrIgnore(database);
        var request = new Request(await RealNetwork.BritishNationalGrid(dataSource, latitude, longitude), distanceKm);
        var slices = new PostgisNetworkSlices(dataSource);
        await slices.AroundAsync(request.Start, 1000);   // a warm connection and plan cache

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var clock = Stopwatch.StartNew();
        var slice = await slices.AroundAsync(request.Start, request.SliceRadiusM);
        var loading = clock.Elapsed;
        var bytes = GC.GetTotalMemory(forceFullCollection: true) - before;

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        clock.Restart();
        var loop = LoopGenerator.Generate(slice, request, ScoringWeights.Default);
        var generating = clock.Elapsed;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        TestContext.Out.WriteLine(
            $"{database ?? "cutthrough (Great Britain)"} · {distanceKm:0} km from {start}: slice of {request.SliceRadiusM / 1000:0.0} km radius, " +
            $"{slice.Segments.Count:N0} Segments, {slice.NodeCount:N0} junctions, loaded in {loading.TotalSeconds:0.00} s, " +
            $"{bytes / 1048576.0:0} MB managed; Loop {(loop is null ? "none" : $"{Breakdown.Of(slice, loop).LengthM / 1000:0.0} km")} " +
            $"in {generating.TotalSeconds:0.0} s, {allocated / 1048576.0:0} MB allocated; " +
            $"process peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:0} MB");
        GC.KeepAlive(slice);
    }
}
