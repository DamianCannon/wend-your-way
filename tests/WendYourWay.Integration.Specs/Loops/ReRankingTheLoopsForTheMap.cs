using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// Moving the Hills slider re-ranks the Loops a Request found, with no new search (PLAN T053,
/// R10.4): on the real London Network, from Kenwood House.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class ReRankingTheLoopsForTheMap
{
    private static readonly LatLon KenwoodHouse = new(51.5712, -0.1677);

    private NpgsqlDataSource _dataSource = null!;
    private LoopPlanner _planner = null!;
    private IOfferedLoops _offered = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore();
        _planner = new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default, RealNetwork.Terrain());
        _offered = await _planner.PlanAsync(KenwoodHouse, distanceKm: 10);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    private static double ClimbPerKm(PlannedLoop loop) => loop.Breakdown.ClimbM / loop.Breakdown.LengthM * 1000;

    [Test]
    public async Task At_the_Lean_it_was_found_with_the_same_Loops_are_offered_without_tracing_them_again() =>
        Assert.That(await _offered.ReRankAsync(new Leans(Hills: 0)), Is.EqualTo(_offered).Using<PlannedLoop>(ReferenceEquals));

    [Test]
    public async Task Leant_to_more_hills_Loop_1_climbs_at_least_as_much()
    {
        var leant = await _offered.ReRankAsync(new Leans(Hills: 1));
        Assert.That(ClimbPerKm(leant[0]), Is.GreaterThanOrEqualTo(ClimbPerKm(_offered[0])));
    }

    [Test]
    public async Task Leant_away_and_back_the_Loops_first_offered_are_offered_again()
    {
        var away = await _offered.ReRankAsync(new Leans(Hills: -1));
        Assert.That(await away.ReRankAsync(new Leans(Hills: 0)), Is.EqualTo(_offered).Using<PlannedLoop>(ReferenceEquals));
    }

    //  The background search (T053b): Loops found at the new Lean on the same slice join those kept.
    //  Not necessarily the Loop a Request there offers first: one found at the centre may score
    //  better at the new Lean too, and keep it out by heading or ground shared.
    [Test]
    public async Task Searched_at_a_new_Lean_Loops_a_Request_there_finds_are_offered()
    {
        var offered = await _planner.PlanAsync(KenwoodHouse, distanceKm: 10);
        var searched = await offered.SearchAsync(new Leans(Hills: 1));
        var there = await _planner.PlanAsync(KenwoodHouse, distanceKm: 10, leans: new Leans(Hills: 1));
        Assert.That(searched.Where(l => there.Any(t => t.Line.SequenceEqual(l.Line))), Is.Not.Empty);
    }

    //  Searching takes a second or more from Kenwood House; not searching again, a moment.
    [Test]
    public async Task Searched_a_second_time_at_the_same_Lean_the_same_Loops_are_offered_at_once()
    {
        var offered = await _planner.PlanAsync(KenwoodHouse, distanceKm: 10);
        var searched = await offered.SearchAsync(new Leans(Hills: -1));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var again = await searched.SearchAsync(new Leans(Hills: -1));
        Assert.Multiple(() =>
        {
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(0.5)));
            Assert.That(again, Is.EqualTo(searched).Using<PlannedLoop>(ReferenceEquals));
        });
    }

    [Test]
    public async Task After_a_search_re_ranking_at_that_Lean_offers_what_the_search_did()
    {
        var offered = await _planner.PlanAsync(KenwoodHouse, distanceKm: 10);
        var searched = await offered.SearchAsync(new Leans(Hills: 0.5));
        Assert.That(await offered.ReRankAsync(new Leans(Hills: 0.5)), Is.EqualTo(searched).Using<PlannedLoop>(ReferenceEquals));
    }
}
