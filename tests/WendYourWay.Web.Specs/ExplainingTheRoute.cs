using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using static WendYourWay.Web.Specs.SampleLoops;

namespace WendYourWay.Web.Specs;

/// <summary>
/// Explain? (T157, #161; user: "I actually like the map labels in their own right" … "Maybe the switch
/// should just say 'Explain?' and also it should update when a route is nudged"): the route's notes
/// on the map, in words, while it's on; kept as last set.
/// </summary>
public class ExplainingTheRoute
{
    private static readonly LatLon Corner = new(51.5650, -0.1500);

    private static RouteNote Note(NoteKind kind, Func<LoopNote, LoopNote>? with = null) =>
        new(Corner, (with ?? (n => n))(new LoopNote(kind, 0, NotePlace.Middle)));

    private static readonly PlannedLoop Explained = Planned with
    {
        Notes = [Note(NoteKind.Crossing, n => n with { Crossing = CrossingKind.Signals, MainRoad = true }), Note(NoteKind.Green, n => n with { LengthM = 2400 })],
    };

    private static readonly PlannedLoop Nudged = Planned with
    {
        Line = [KenwoodHouse, Corner, KenwoodHouse],
        Notes = [Note(NoteKind.Obstacle, n => n with { Obstacle = ObstacleKind.Stile })],
    };

    private FakePlanner _planner = null!;
    private FakeLastRequest _last = null!;
    private RecordingMap _map = null!;
    private PlannerSession _session = null!;

    [SetUp]
    public Task Arrange() => Open(new FakeLastRequest());

    private async Task Open(FakeLastRequest last)
    {
        (_planner, _last, _map) = (new FakePlanner { Answer = Explained }, last, new RecordingMap());
        _session = new PlannerSession(_planner, new FakeSavedRoutes(), new FakeMarks(), new PlannerSettings(KenwoodHouse), _last,
            new FakeCarParks(), new FakePreviews(), new FakePlaceSearch(), new FakeRouteTurns(), new FakePhoneShare(),
            NullLogger<PlannerSession>.Instance);
        await _session.InitializeAsync();
        await _session.MapReadyAsync(_map);
    }

    [TearDown]
    public void Dispose() => _session.Dispose();

    private IEnumerable<string> Said => ((IReadOnlyList<NoteOnMap>)_map.Last("showNotes")!).Select(n => n.Words);

    [Test]
    public async Task Off_at_first_the_map_says_nothing()
    {
        await _session.GoAsync();
        Assert.That((_session.Explaining, Said), Is.EqualTo((false, Array.Empty<string>())));
    }

    [Test]
    public async Task On_the_map_says_the_route_s_notes()
    {
        await _session.GoAsync();
        await _session.ToggleExplainAsync();
        Assert.That(Said, Is.EqualTo(new[] { "Crossing with lights", "Longest green: 2.40 km" }));
    }

    [Test]
    public async Task Off_again_it_says_nothing()
    {
        await _session.GoAsync();
        await _session.ToggleExplainAsync();
        await _session.ToggleExplainAsync();
        Assert.That(Said, Is.Empty);
    }

    [Test]
    public async Task Nudged_it_says_the_nudged_route_s()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Nudged, 0);
        await _session.GoAsync();
        await _session.ToggleExplainAsync();
        await _session.NudgeAsync(new AvoidNudge(ParliamentHill));
        Assert.That(Said, Is.EqualTo(new[] { "Stile" }));
    }

    [Test]
    public async Task Paged_it_says_the_next_route_s()
    {
        _planner.Answers = [Explained, Nudged];
        await _session.GoAsync();
        await _session.ToggleExplainAsync();
        await _session.PageAsync(1);
        Assert.That(Said, Is.EqualTo(new[] { "Stile" }));
    }

    [Test]
    public async Task Undone_it_says_the_route_before_s()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Nudged, 0);
        await _session.GoAsync();
        await _session.ToggleExplainAsync();
        await _session.NudgeAsync(new AvoidNudge(ParliamentHill));
        await _session.UndoAsync();
        Assert.That(Said, Is.EqualTo(new[] { "Crossing with lights", "Longest green: 2.40 km" }));
    }

    [Test]
    public async Task On_it_s_on_next_time()
    {
        await _session.ToggleExplainAsync();
        var kept = _last.Saved;
        _session.Dispose();
        await Open(new FakeLastRequest(kept));
        await _session.GoAsync();
        Assert.That((_session.Explaining, Said.Count()), Is.EqualTo((true, 2)));
    }

    [Test]
    public async Task The_card_shows_whether_it_s_on()
    {
        await _session.ToggleExplainAsync();
        Assert.That(_session.Pager.Explaining, Is.True);
    }
}

/// <summary>What each note says on the map (T157), in the Loop card's words, and from which zoom.</summary>
public class SayingTheNotes
{
    private static NoteOnMap Said(LoopNote note) => NoteOnMap.Of(new RouteNote(new LatLon(51.5, -0.1), note));

    private static LoopNote A(NoteKind kind) => new(kind, 0, NotePlace.Middle);

    [TestCase(CrossingKind.Signals, false, "Crossing with lights")]
    [TestCase(CrossingKind.Marked, false, "Zebra crossing")]
    [TestCase(CrossingKind.Unmarked, false, "Across the road")]
    [TestCase(CrossingKind.Unmarked, true, "Across a Main Road, no crossing")]
    public void A_Crossing_by_its_kind(CrossingKind kind, bool mainRoad, string words) =>
        Assert.That(Said(A(NoteKind.Crossing) with { Crossing = kind, MainRoad = mainRoad }).Words, Is.EqualTo(words));

    [Test]
    public void An_Obstacle_by_its_kind() =>
        Assert.That(Said(A(NoteKind.Obstacle) with { Obstacle = ObstacleKind.KissingGate }).Words, Is.EqualTo("Kissing gate"));

    [Test]
    public void A_stretch_of_road_by_its_length() =>
        Assert.That(Said(A(NoteKind.Road) with { LengthM = 450 }).Words, Is.EqualTo("450 m of road"));

    [Test]
    public void A_Main_Road_s_pavement_says_so() =>
        Assert.That(Said(A(NoteKind.Road) with { LengthM = 450, MainRoadM = 120, Pavement = true }).Words, Is.EqualTo("450 m of road, 120 m beside a Main Road"));

    [Test]
    public void A_Main_Road_s_carriageway_says_on_it() =>
        Assert.That(Said(A(NoteKind.Road) with { LengthM = 1250, MainRoadM = 1250 }).Words, Is.EqualTo("1.25 km of road, all on a Main Road"));

    [Test]
    public void The_longest_green_stretch_as_the_card_says_it() =>
        Assert.That(Said(A(NoteKind.Green) with { LengthM = 3204 }).Words, Is.EqualTo("Longest green: 3.20 km"));

    //  Zoomed out, the stretches; zoomed in to streets, each Crossing and Obstacle too.
    [Test]
    public void Crossings_and_Obstacles_show_zoomed_in_further_than_stretches() =>
        Assert.That(Said(A(NoteKind.Crossing)).FromZoom, Is.GreaterThan(Said(A(NoteKind.Road)).FromZoom).And.EqualTo(Said(A(NoteKind.Obstacle)).FromZoom));

    [Test]
    public void It_s_where_the_note_is() =>
        Assert.That(Said(A(NoteKind.Green)).At, Is.EqualTo(new LatLon(51.5, -0.1)));
}
