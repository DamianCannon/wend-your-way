using System.Diagnostics;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// STORY-029 on the real Greater London Network: a 10 km Run from Kenwood House, loaded and
/// generated the way the app will do it.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class GeneratingALoopInLondon
{
    private NpgsqlDataSource _dataSource = null!;
    private Request _request = null!;
    private NetworkSlice _slice = null!;
    private Loop? _loop;
    private TimeSpan _loading;
    private TimeSpan _generating;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore();
        _request = new Request(await RealNetwork.BritishNationalGrid(_dataSource, 51.5712, -0.1677), DistanceKm: 10);

        var clock = Stopwatch.StartNew();
        _slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(_request.Start, _request.SliceRadiusM);
        _loading = clock.Elapsed;
        clock.Restart();
        _loop = LoopGenerator.Generate(_slice, _request, ScoringWeights.Default);
        _generating = clock.Elapsed;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    [Test]
    public void Then_a_Loop_comes_back()   // AC1
    {
        Assert.That(_loop, Is.Not.Null);
        var breakdown = Breakdown.Of(_slice, _loop!);
        TestContext.Out.WriteLine(
            $"{breakdown.LengthM / 1000:0.00} km · Off-road {breakdown.OffRoadPercent:0}% · Traffic-free {breakdown.TrafficFreePercent:0}% · " +
            $"Connector {breakdown.ConnectorLengthM / 1000:0.00} km · score {LoopScorer.Score(_slice, _loop!, ScoringWeights.Default):0.000} · " +
            $"slice {_slice.Segments.Count:N0} Segments loaded in {_loading.TotalSeconds:0.00} s, generated in {_generating.TotalSeconds:0.00} s");
    }

    [Test]
    public void Then_it_starts_near_the_start_point_and_finishes_where_it_started()   // AC2
    {
        Assert.That(_slice.Node(_loop!.Start).DistanceTo(_request.Start), Is.LessThan(Request.SnapMarginM));
        var at = _loop.Start;
        foreach (var traversal in _loop.Traversals)
        {
            var segment = _slice.Segments[traversal.Segment];
            at = traversal.Forward ? segment.To : segment.From;
        }
        Assert.That(at, Is.EqualTo(_loop.Start));
    }

    [Test]
    public void Then_its_length_is_between_9_and_11_km() =>   // AC2
        Assert.That(Breakdown.Of(_slice, _loop!).LengthM, Is.InRange(9000, 11500));

    [Test]
    public void Then_each_Segment_follows_on_from_the_last_and_none_is_forbidden()   // AC3
    {
        var at = _loop!.Start;
        foreach (var traversal in _loop.Traversals)
        {
            var segment = _slice.Segments[traversal.Segment];
            Assert.That(traversal.Forward ? segment.From : segment.To, Is.EqualTo(at), $"{segment.Key} doesn't start where the last ended");
            Assert.That(segment.Access, Is.Not.EqualTo(AccessKind.Forbidden), $"{segment.Key} is forbidden for a Run");
            at = traversal.Forward ? segment.To : segment.From;
        }
    }

    [Test]
    public void Then_it_is_ready_well_within_the_time_budget() =>   // AC5, R1.6
        Assert.That(_loading + _generating, Is.LessThan(TimeSpan.FromSeconds(10)));
}

/// <summary>Not just one lucky start: 10 km Runs from public places all over Greater London.</summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class GeneratingLoopsAcrossLondon
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange() => _dataSource = await RealNetwork.OpenOrIgnore();

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    [TestCase("Regent's Park, the Hub", 51.5294, -0.1560)]
    [TestCase("Greenwich Park, the Observatory", 51.4769, -0.0005)]
    [TestCase("Victoria Park, Hackney", 51.5362, -0.0396)]
    [TestCase("Wimbledon Common, the windmill", 51.4378, -0.2311)]
    [TestCase("Richmond Park, Pembroke Lodge", 51.4436, -0.2963)]
    [TestCase("Trafalgar Square", 51.5080, -0.1281)]
    [TestCase("Epping Forest, Queen Elizabeth's Hunting Lodge", 51.6334, 0.0004)]
    //  Before same ground (T014), a Run from near here went out and back about 3 km along
    //  Lea Bridge Road's cycle tracks, one on each side.
    [TestCase("Lea Bridge station", 51.5667, -0.0365)]
    public async Task A_10_km_Run_comes_back_within_the_window_without_a_long_Retrace(string place, double latitude, double longitude)
    {
        var request = new Request(await RealNetwork.BritishNationalGrid(_dataSource, latitude, longitude), DistanceKm: 10);
        var clock = Stopwatch.StartNew();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(request.Start, request.SliceRadiusM);
        var loop = LoopGenerator.Generate(slice, request, ScoringWeights.Default);
        var took = clock.Elapsed;

        Assert.That(loop, Is.Not.Null, $"no Loop from {place}");
        var breakdown = Breakdown.Of(slice, loop!);
        // Fiddliness, which the score can't see (T039): pieces of Open ground, and Segments a km.
        var openGroundPieces = loop!.Traversals.Select(t => slice.Segments[t.Segment].Key.IsOpenGround)
            .Zip(loop.Traversals.Skip(1).Select(t => slice.Segments[t.Segment].Key.IsOpenGround).Append(false))
            .Count(p => p.First && !p.Second);
        await TestContext.Out.WriteLineAsync(
            $"{place}: {breakdown.LengthM / 1000:0.00} km · Off-road {breakdown.OffRoadPercent:0}% · Traffic-free {breakdown.TrafficFreePercent:0}% · " +
            $"Connector {breakdown.ConnectorLengthM / 1000:0.00} km · Main Road {breakdown.MainRoadLengthM / 1000:0.00} km · Flow {breakdown.FlowM / 1000:0.00} km · Greenery {breakdown.GreeneryPercent:0}% · Waterside {breakdown.WatersidePercent:0}% · Crossings {breakdown.Crossings} ({breakdown.MainRoadCrossings} of Main Roads) · Corridor {breakdown.CorridorLengthM / 1000:0.00} km · " +
            $"Open ground {breakdown.OpenGroundM / 1000:0.00} km in {openGroundPieces} · {loop.Traversals.Count / (breakdown.LengthM / 1000):0} Segments a km · " +
            $"Retrace {breakdown.RetraceLengthM / 1000:0.00} km · score {LoopScorer.Score(slice, loop!, ScoringWeights.Default):0.000} · " +
            $"start snapped {slice.Node(loop!.Start).DistanceTo(request.Start):0} m · {took.TotalSeconds:0.0} s");
        Assert.That(breakdown.LengthM, Is.InRange(9000, 11500));
        //  Up to 1.5 km: the score charges ground covered again twice its rate, not four times,
        //  since the user rates out and back along a canal good; a spur is a Knot instead (T042).
        Assert.That(breakdown.RetraceLengthM, Is.LessThan(1500));
        Assert.That(took, Is.LessThan(TimeSpan.FromSeconds(10)));
    }
}
