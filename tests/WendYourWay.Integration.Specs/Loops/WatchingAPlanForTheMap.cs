using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// The planner says how far a Request has got (PLAN T054): reading the map, trying Loops, drawing
/// them; each Loop tried as a faint line; and the Loops offered so far, before it ends. On the real
/// London Network, 10 km from Kenwood House.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class WatchingAPlanForTheMap
{
    private static readonly LatLon KenwoodHouse = new(51.5712, -0.1677);

    private NpgsqlDataSource _dataSource = null!;
    private readonly List<PlanningProgress> _seen = [];
    private IOfferedLoops _offered = null!;
    private IOfferedLoops _unwatched = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore();
        //  Read as slowly as from a cold database (T125): on a warm one, London's slice can be read
        //  between two of the planner's reports, and there's no time left to say (failed twice so).
        var planner = new LoopPlanner(new SlowToRead(new PostgisNetworkSlices(_dataSource)), new MapGeometry(_dataSource), ScoringWeights.Default);
        _offered = await planner.PlanAsync(KenwoodHouse, distanceKm: 10, progress: _seen.Add);
        _unwatched = await planner.PlanAsync(KenwoodHouse, distanceKm: 10);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    //  A read as a cold database's starts: its first page told, then longer than the planner takes
    //  between reports before the rest. What the reader itself tells is ReadingASlice's.
    private sealed class SlowToRead(INetworkSlices inner) : INetworkSlices
    {
        public async Task<NetworkSlice> AroundAsync(NodePoint centre, double radiusM, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run,
            Action<SliceReading>? reading = null, CancellationToken cancellationToken = default)
        {
            reading?.Invoke(new SliceReading(1, 1_000));
            await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken);
            return await inner.AroundAsync(centre, radiusM, activity, reading, cancellationToken);
        }
    }

    [Test]
    public void Then_it_reads_the_map_tries_Loops_and_draws_them_in_that_order() =>
        Assert.That(_seen.Select(p => p.Stage).Distinct(), Is.EqualTo(new[] { PlanningStage.Reading, PlanningStage.Trying, PlanningStage.Drawing }));

    [Test]
    public void Then_reading_the_map_ends_with_all_of_it_read() =>
        Assert.That(_seen.Last(p => p.Stage == PlanningStage.Reading) is { Total: { } total } last && last.Done == total, Is.True);

    [Test]
    public void Then_trying_Loops_ends_with_all_of_them_tried() =>
        Assert.That(_seen.Last(p => p.Stage == PlanningStage.Trying) is { Total: { } total } last && last.Done == total, Is.True);

    [Test]
    public void Then_Loops_are_offered_before_it_ends() =>
        Assert.That(_seen.Where(p => p.Stage == PlanningStage.Trying).Any(p => p.SoFar is { Count: > 0 }), Is.True);

    [Test]
    public void Then_while_reading_it_says_how_long_is_left() =>
        Assert.That(_seen.Where(p => p.Stage == PlanningStage.Reading && p.Done < p.Total).Any(p => p.Left > TimeSpan.Zero), Is.True);

    [Test]
    public void Then_while_trying_it_doesn_t_guess_how_long_is_left() =>
        Assert.That(_seen.Where(p => p.Stage == PlanningStage.Trying).Select(p => p.Left), Is.All.Null);

    [Test]
    public void Then_it_offers_what_it_offers_unwatched() =>
        Assert.That(_offered.Select(l => l.Line), Is.EqualTo(_unwatched.Select(l => l.Line)));

    //  A new start cancels the Request: it stops reporting at once rather than going round its
    //  wait without waiting (review of T054), and says it was cancelled.
    [Test]
    public async Task Cancelled_while_trying_Loops_it_stops_at_once()
    {
        var planner = new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default);
        using var cancel = new CancellationTokenSource();
        var afterwards = 0;
        void Watch(PlanningProgress p)
        {
            if (cancel.IsCancellationRequested) afterwards++;
            else if (p.Stage == PlanningStage.Trying) cancel.Cancel();
        }
        Assert.Multiple(() =>
        {
            Assert.That(async () => await planner.PlanAsync(KenwoodHouse, distanceKm: 10, progress: Watch, cancellationToken: cancel.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(afterwards, Is.LessThanOrEqualTo(1));
        });
    }
}
