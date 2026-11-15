using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// When a Saved Route matched onto a new map is the same route (PLAN T136): each line within 25 m of
/// the other, their lengths within 2%, no gap. Each told apart here, the match a stand-in's: a
/// saved 2 km line east from an invented point, flagged since none of its Segments is on the
/// (empty) Network.
/// </summary>
[TestFixture]
[NonParallelizable]
public class TellingTheSameRoute
{
    private const double MetresEast = 1 / (111_320 * 0.6225);  // degrees of longitude a metre at 51.5° N

    private NpgsqlDataSource _dataSource = null!;
    private PostgisSavedRoutes _store = null!;

    [OneTimeSetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _store = new PostgisSavedRoutes(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    //  East from the invented point, a point every 100 m, this far north of it (metres) at each.
    private static PlannedLoop Line(Func<int, double> north, int points = 21) => new(
        [.. Enumerable.Range(0, points).Select(i => new LatLon(51.5 + (north(i) / 111_320), -0.1 + (i * 100 * MetresEast)))],
        new LatLon(51.5, -0.1), [], new Breakdown(LengthM: (points - 1) * 100, OffRoadPercent: 50, TrafficFreePercent: 50, ConnectorLengthM: 0, RetraceLengthM: 0));

    private static readonly PlannedLoop Saved = Line(_ => 0);

    private async Task<RouteMove> Matched(PlannedLoop matched, PlannedLoop? saved = null)
    {
        var id = await _store.SaveAsync("East", saved ?? Saved, [(new SegmentKey(1, 2, 3), true)]);
        return await new SavedRouteMoves(_store, new MatchesAs(matched), _dataSource).MoveAsync(id);
    }

    [Test]
    public async Task Ten_metres_off_it_s_the_same_route() =>
        Assert.That((await Matched(Line(_ => 10))).Outcome, Is.EqualTo(RouteMoveOutcome.Moved));

    [Test]
    public async Task Forty_metres_off_it_isn_t() =>
        Assert.That(await Matched(Line(i => i >= 10 ? 40 : 0)), Is.EqualTo(new RouteMove(RouteMoveOutcome.Kept, "From 1.0 km it no longer follows the map.")));

    //  Within 25 m all the way, but zig-zagging 24 m: 2.8% longer.
    [Test]
    public async Task Three_percent_longer_it_isn_t() =>
        Assert.That((await Matched(Line(i => i % 2 == 0 ? 0 : 24))).Outcome,
            Is.EqualTo(RouteMoveOutcome.Kept));

    [Test]
    public async Task With_a_gap_it_isn_t_and_says_where() =>
        Assert.That(await Matched(Line(_ => 0) with { Gaps = [new RouteGap([new LatLon(51.5, -0.1 + (1_200 * MetresEast)), new LatLon(51.5, -0.1 + (1_300 * MetresEast))], 100)] }),
            Is.EqualTo(new RouteMove(RouteMoveOutcome.Kept, "From 1.2 km it no longer follows the map.")));

    //  Changed with the user (T136, "50 m on old gaps"): where it was off the map when saved (its
    //  GPS trace, from 0.8 to 1.2 km here), the path mapped since may be up to 50 m from it.
    private static readonly PlannedLoop SavedWithAGap = Saved with
    {
        Gaps = [new RouteGap([.. Enumerable.Range(8, 5).Select(i => new LatLon(51.5, -0.1 + (i * 100 * MetresEast)))], 400)],
    };

    [Test]
    public async Task Forty_metres_off_where_it_was_off_the_map_it_s_the_same_route() =>
        Assert.That((await Matched(Line(i => i is > 8 and < 12 ? 40 : 0), SavedWithAGap)).Outcome, Is.EqualTo(RouteMoveOutcome.Moved));

    [Test]
    public async Task Sixty_metres_off_there_it_isn_t() =>
        Assert.That((await Matched(Line(i => i is > 8 and < 12 ? 60 : 0), SavedWithAGap)).Outcome, Is.EqualTo(RouteMoveOutcome.Kept));

    [Test]
    public async Task Forty_metres_off_elsewhere_it_still_isn_t() =>
        Assert.That((await Matched(Line(i => i is > 14 and < 18 ? 40 : 0), SavedWithAGap)).Outcome, Is.EqualTo(RouteMoveOutcome.Kept));

    //  The matcher's stand-in: the saved line matched as given.
    private sealed class MatchesAs(PlannedLoop matched) : ILoopPlanner
    {
        public Task<IOfferedLoops> ImportAsync(IReadOnlyList<LatLon> route, Activity activity = Activity.Run, CancellationToken cancellationToken = default) =>
            Task.FromResult<IOfferedLoops>(new Offer(matched));

        public Task<IOfferedLoops> PlanAsync(LatLon start, double distanceKm, Leans? leans = null, Activity activity = Activity.Run, Action<PlanningProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IOfferedLoops> PlanPointToPointAsync(LatLon start, LatLon finish, double? distanceKm = null, Leans? leans = null, Activity activity = Activity.Run, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IOfferedLoops> ReopenAsync(PlannedLoop saved, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, Activity activity = Activity.Run, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Offer(PlannedLoop loop) : IOfferedLoops
    {
        public PlannedLoop this[int index] => index == 0 ? loop : throw new ArgumentOutOfRangeException(nameof(index));

        public int Count => 1;

        public IEnumerator<PlannedLoop> GetEnumerator() => new[] { loop }.AsEnumerable().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public IReadOnlyList<(SegmentKey Key, bool Forward)>? TraversalsOf(PlannedLoop planned) => [(new SegmentKey(4, 5, 6), true)];

        public Task<IOfferedLoops> ReRankAsync(Leans leans, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IOfferedLoops> SearchAsync(Leans leans, Action<PlanningProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<NudgedLoop?> NudgeAsync(PlannedLoop planned, LoopNudge nudge, Leans leans, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SegmentKey?> SegmentAtAsync(PlannedLoop planned, LatLon at, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
