using WendYourWay.Core.Places;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using static WendYourWay.Web.Specs.SampleLoops;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The planner's session (T124c), with no page: the same fakes the page's specs use, a map that
/// keeps its calls, and the session's state read directly. What the page's 395 specs show through
/// the screen, a few of the transitions here at the speed of plain code.
/// </summary>
public class PlanningInASession
{
    private static readonly FoundStart PembrokeLodge = new("Pembroke Lodge", new LatLon(51.4436, -0.2963), 338_000);

    private FakePlanner _planner = null!;
    private FakeSavedRoutes _saved = null!;
    private RecordingMap _map = null!;
    private PlannerSession _session = null!;
    private int _changed;

    [SetUp]
    public async Task Arrange()
    {
        (_planner, _saved, _map) = (new FakePlanner { Answer = Planned }, new FakeSavedRoutes(), new RecordingMap());
        _session = new PlannerSession(_planner, _saved, new FakeMarks(), new PlannerSettings(KenwoodHouse), new FakeLastRequest(),
            new FakeCarParks(), new FakePreviews(), new FakePlaceSearch(), new FakeRouteTurns(), new FakePhoneShare(),
            NullLogger<PlannerSession>.Instance);
        _session.Changed += () => _changed++;
        await _session.InitializeAsync();
        await _session.MapReadyAsync(_map);
    }

    [TearDown]
    public void Dispose() => _session.Dispose();

    [Test]
    public void It_starts_at_home() => Assert.That((_session.Start, _session.StartLabel), Is.EqualTo(((LatLon?)KenwoodHouse, "Home")));

    [Test]
    public void Ready_the_map_shows_the_start() => Assert.That(_map.Last("showStart"), Is.EqualTo(KenwoodHouse));

    [Test]
    public async Task Go_asks_for_Loops_of_the_Distance_from_the_start()
    {
        _session.DistanceKm = 12;
        await _session.GoAsync();
        Assert.That(_planner.Asked, Is.EqualTo(new[] { (KenwoodHouse, 12.0) }));
    }

    [Test]
    public async Task Go_offers_the_Loops_found_and_shows_the_first()
    {
        await _session.GoAsync();
        Assert.That((_session.Loops, _session.Current, _session.Finding), Is.EqualTo((new[] { Planned }, 0, false)));
    }

    //  The last Loop off the map at once, then the new one drawn and fitted.
    [Test]
    public async Task Go_clears_the_map_then_draws_the_Loop_fitted()
    {
        _map.Calls.Clear();
        await _session.GoAsync();
        Assert.That((_map.Names.ToArray(), _map.Last("showLoop")), Is.EqualTo((new[] { "clearLoop", "showLoop" }, (object)(Planned, true))));
    }

    [Test]
    public async Task Go_says_what_changed() // for the page to show it
    {
        _changed = 0;
        await _session.GoAsync();
        Assert.That(_changed, Is.GreaterThan(0));
    }

    [Test]
    public async Task A_Distance_out_of_range_isnt_asked_for_and_says_why()
    {
        _session.DistanceKm = 0.5;
        await _session.GoAsync();
        Assert.That((_planner.Asked.Count, _session.Problem), Is.EqualTo((0, "Choose a Distance from 1 to 100 km.")));
    }

    [Test]
    public async Task A_car_park_chosen_is_the_start_and_its_Loops_are_found()
    {
        await _session.CarParkChosenAsync(PembrokeLodge);
        Assert.That((_session.Start, _session.StartLabel, _planner.Asked.Single().Start), Is.EqualTo(((LatLon?)PembrokeLodge.At, "Pembroke Lodge", PembrokeLodge.At)));
    }

    //  A Request in flight is dropped when another start is chosen: no Loop for an old start.
    [Test]
    public async Task A_new_start_cancels_the_Request_in_flight()
    {
        _planner.Pending = new TaskCompletionSource<PlannedLoop?>();
        var first = _session.GoAsync();
        _planner.Pending = null;
        await _session.CarParkChosenAsync(PembrokeLodge);
        Assert.That(_planner.Tokens[0].IsCancellationRequested, Is.True);
        _ = first;
    }

    [Test]
    public async Task Undo_brings_back_the_Loops_before_and_their_start()
    {
        await _session.GoAsync();
        _planner.Answer = Second;
        await _session.CarParkChosenAsync(PembrokeLodge);
        await _session.UndoAsync();
        Assert.That((_session.Loops.Single(), _session.Start, _map.Last("showLoop")), Is.EqualTo((Planned, (LatLon?)KenwoodHouse, (object)(Planned, true))));
    }

    [Test]
    public async Task Redo_goes_forward_again()
    {
        await _session.GoAsync();
        _planner.Answer = Second;
        await _session.CarParkChosenAsync(PembrokeLodge);
        await _session.UndoAsync();
        await _session.RedoAsync();
        Assert.That((_session.Loops.Single(), _session.CanRedo), Is.EqualTo((Second, false)));
    }

    //  T112: saved by a name, it's exported by it; nudged, it keeps it.
    [Test]
    public async Task Saved_by_a_name_and_nudged_it_is_exported_by_that_name()
    {
        var nudged = Planned with { Line = [KenwoodHouse, PembrokeLodge.At, KenwoodHouse] };
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(nudged, 0);
        await _session.GoAsync();
        await _session.SaveAsync("Sunday hills");
        await _session.NudgeAsync(new AvoidNudge(ParliamentHill));
        Assert.That((_session.Loops.Single(), _session.ExportedAs), Is.EqualTo((nudged, "Sunday hills")));
    }

    [Test]
    public async Task A_Nudge_with_no_way_says_so_and_leaves_the_Loop()
    {
        _planner.Leaning.Nudged = (_, _) => null;
        await _session.GoAsync();
        await _session.NudgeAsync(new AvoidNudge(ParliamentHill));
        Assert.That((_session.Loops.Single(), _session.NudgeNote), Is.EqualTo((Planned, "No way round there: the Loop is as it was")));
    }

    [Test]
    public async Task A_Saved_Route_opened_is_shown_alone_by_its_name()
    {
        await _session.OpenSavedAsync(new SavedRoute(4, "Sunday hills", Second));
        Assert.That((_session.Loops.Single(), _session.ExportedAs, _map.Last("showLoop")), Is.EqualTo((Second, "Sunday hills", (object)(Second, true))));
    }

    [Test]
    public async Task An_imported_route_is_shown_and_offered_its_name()
    {
        _planner.Imported = Third;
        var problem = await _session.ImportAsync(new RouteGpx("Heath figure of eight", [KenwoodHouse, ParliamentHill]));
        Assert.That((problem, _session.Loops.Single(), _session.SuggestedName), Is.EqualTo(((string?)null, Third, "Heath figure of eight")));
    }

    //  T124e: what the Loop card shows, made by the session.
    [Test]
    public async Task The_card_shows_the_Loops_offered_and_what_Save_would_call_one()
    {
        await _session.GoAsync();
        var pager = _session.Pager;
        Assert.That((pager.Loops, pager.Current, pager.Finding, pager.SuggestedName, pager.Place), Is.EqualTo((new[] { Planned }, 0, false, "Home · 9.7 km", (string?)null)));
    }

    [Test]
    public async Task The_card_names_the_car_park_started_from()
    {
        await _session.CarParkChosenAsync(PembrokeLodge);
        Assert.That(_session.Pager.Place, Is.EqualTo("Pembroke Lodge"));
    }

    //  The answer is the panel's that asked (the Saved Routes or Outings panel), not the pager card's.
    [Test]
    public async Task An_import_that_cant_be_matched_answers_why_and_keeps_what_was_shown()
    {
        await _session.GoAsync();
        _planner.ImportFailure = new RouteNotMatchedException("Under half of that route follows the map.");
        var problem = await _session.ImportAsync(new RouteGpx("Somewhere", [KenwoodHouse, ParliamentHill]));
        Assert.That((problem, _session.Loops.Single()), Is.EqualTo(("Under half of that route follows the map.", Planned)));
    }
}
