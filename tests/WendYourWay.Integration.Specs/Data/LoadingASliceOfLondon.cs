using System.Diagnostics;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The real thing: a 10 km Request's slice of the Greater London Network, built by
/// `wend network build` into the app's own database, which this spec only ever reads. Skipped when
/// that database has no Network yet.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class LoadingASliceOfLondon
{
    private NpgsqlDataSource _dataSource = null!;
    private NetworkSlice _slice = null!;
    private TimeSpan _loadTime;
    private long _bytes;
    private NodePoint _kenwoodHouse;
    private NodePoint _parliamentHill;
    private double _radiusM;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore();
        _kenwoodHouse = await RealNetwork.BritishNationalGrid(_dataSource, 51.5712, -0.1677);
        _parliamentHill = await RealNetwork.BritishNationalGrid(_dataSource, 51.5597, -0.1607);

        _radiusM = new Request(_kenwoodHouse, DistanceKm: 10).SliceRadiusM;
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var clock = Stopwatch.StartNew();
        _slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(_kenwoodHouse, _radiusM);
        _loadTime = clock.Elapsed;
        _bytes = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(_slice);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    private int NearestJunction(NodePoint point) =>
        Enumerable.Range(0, _slice.NodeCount).MinBy(n => _slice.Node(n).DistanceTo(point));

    [Test]
    public void Then_it_loads_quickly_enough_for_the_30_second_budget()
    {
        TestContext.Out.WriteLine(
            $"Slice of {_radiusM / 1000:0.0} km radius: {_slice.Segments.Count:N0} Segments, {_slice.NodeCount:N0} junctions, " +
            $"loaded in {_loadTime.TotalSeconds:0.00} s, {_bytes / 1048576.0:0.0} MB managed memory.");
        Assert.That(_loadTime, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void Then_a_Path_across_Hampstead_Heath_is_connected_and_plausible()
    {
        var from = NearestJunction(_kenwoodHouse);
        var to = NearestJunction(_parliamentHill);
        var path = PathFinder.Find(_slice, PathEnd.AtNode(from), PathEnd.AtNode(to), new RunCostModel(ScoringWeights.Default));

        Assert.That(path, Is.Not.Null, "no Path from Kenwood House to Parliament Hill");
        var at = from;
        foreach (var traversal in path!.Traversals)
        {
            var segment = _slice.Segments[traversal.Segment];
            Assert.That(traversal.Forward ? segment.From : segment.To, Is.EqualTo(at), $"{segment.Key} doesn't start where the last ended");
            Assert.That(segment.Access, Is.Not.EqualTo(AccessKind.Forbidden), $"{segment.Key} is forbidden for a Run");
            at = traversal.Forward ? segment.To : segment.From;
        }
        var straight = _slice.Node(from).DistanceTo(_slice.Node(to));
        Assert.Multiple(() =>
        {
            Assert.That(at, Is.EqualTo(to));
            Assert.That(path.LengthM, Is.InRange(straight, 2 * straight));
        });
        TestContext.Out.WriteLine($"Kenwood House → Parliament Hill: {path.LengthM:N0} m (straight line {straight:N0} m), {path.Traversals.Count} Segments.");
    }
}
