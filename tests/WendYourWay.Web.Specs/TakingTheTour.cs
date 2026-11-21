using WendYourWay.Core.Network;
using WendYourWay.Data;
using WendYourWay.Data.Places;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using static WendYourWay.Web.Specs.SampleLoops;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The first-Loop tour (T158, #162; user: "how to introduce people to WYW … some sort of learning
/// mode?" … "yeah do the first loop tour"): offered once, taken from Help any time, each step moved
/// on by doing it: start → Distance and Go → page → Breakdown → Nudge → keep it.
/// </summary>
public class TakingTheTour
{
    private FakePlanner _planner = null!;
    private FakeLastRequest _last = null!;
    private PlannerSession _session = null!;

    [SetUp]
    public Task Arrange() => Open(new FakeLastRequest());

    private async Task Open(FakeLastRequest last)
    {
        (_planner, _last) = (new FakePlanner { Answers = [Planned, Second] }, last);
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Third, 0);
        _session = new PlannerSession(_planner, new FakeSavedRoutes(), new FakeMarks(), new PlannerSettings(KenwoodHouse), _last,
            new FakeCarParks(), new FakePreviews(), new FakePlaceSearch(), new FakeRouteTurns(), new FakePhoneShare(),
            NullLogger<PlannerSession>.Instance);
        await _session.InitializeAsync();
        await _session.MapReadyAsync(new RecordingMap());
    }

    [TearDown]
    public void Dispose() => _session.Dispose();

    private TourStep? Step => _session.Tour?.Step;

    // Taken, and done as far as the step given.
    private async Task TakenTo(TourStep step)
    {
        await _session.TakeTourAsync();
        if (step > TourStep.Start) await _session.StartChosenOnMapAsync(ParliamentHill);
        if (step > TourStep.Go) await _session.GoPressedAsync();
        if (step > TourStep.Page) await _session.PageAsync(1);
        if (step > TourStep.Breakdown) await _session.BreakdownOpenedAsync();
        if (step > TourStep.Nudge) await _session.NudgeAsync(new AvoidNudge(ParliamentHill));
        Assume.That(Step, Is.EqualTo(step));
    }

    [Test]
    public void Opened_for_the_first_time_it_offers_the_tour() =>
        Assert.That(Step, Is.EqualTo(TourStep.Offered));

    [Test]
    public async Task Opened_since_the_offer_was_answered_it_isnt_offered()
    {
        await Open(new FakeLastRequest(new RequestSettings(null, 10, 0) { TourOffered = true }));
        Assert.That(_session.Tour, Is.Null);
    }

    //  Kept before there was a tour, it's offered once.
    [Test]
    public async Task Opened_with_settings_from_before_the_tour_it_offers_it()
    {
        await Open(new FakeLastRequest(new RequestSettings(null, 10, 0)));
        Assert.That(Step, Is.EqualTo(TourStep.Offered));
    }

    [Test]
    public async Task No_thanks_and_it_isnt_offered_again()
    {
        await _session.NoTourAsync();
        var kept = _last.Saved;
        _session.Dispose();
        await Open(new FakeLastRequest(kept));
        Assert.That(_session.Tour, Is.Null);
    }

    [Test]
    public async Task Shown_it_and_it_isnt_offered_again()
    {
        await _session.TakeTourAsync();
        Assert.That(_last.Saved?.TourOffered, Is.True);
    }

    [Test]
    public async Task Taken_it_starts_at_the_start_one_of_six()
    {
        await _session.TakeTourAsync();
        Assert.That((Step, _session.Tour!.Number, FirstLoopTour.Steps), Is.EqualTo(((TourStep?)TourStep.Start, 1, 6)));
    }

    [Test]
    public async Task A_start_clicked_on_the_map_moves_it_to_Go()
    {
        await TakenTo(TourStep.Start);
        await _session.StartChosenOnMapAsync(ParliamentHill);
        Assert.That(Step, Is.EqualTo(TourStep.Go));
    }

    [Test]
    public async Task A_car_park_chosen_moves_it_to_Go_too()
    {
        await TakenTo(TourStep.Start);
        await _session.CarParkChosenAsync(new FoundStart("Car park", ParliamentHill, 0));
        Assert.That(Step, Is.EqualTo(TourStep.Go));
    }

    //  A start found from at once, so Go is pressed after: only Go itself moves it on.
    [Test]
    public async Task Go_pressed_moves_it_to_paging()
    {
        await TakenTo(TourStep.Go);
        await _session.GoPressedAsync();
        Assert.That(Step, Is.EqualTo(TourStep.Page));
    }

    [Test]
    public async Task Go_pressed_with_the_start_as_it_was_moves_it_past_the_start_too()
    {
        await TakenTo(TourStep.Start);
        await _session.GoPressedAsync();
        Assert.That(Step, Is.EqualTo(TourStep.Page));
    }

    [Test]
    public async Task Paged_it_moves_to_the_Breakdown()
    {
        await TakenTo(TourStep.Page);
        await _session.PageAsync(1);
        Assert.That(Step, Is.EqualTo(TourStep.Breakdown));
    }

    [Test]
    public async Task The_Breakdown_opened_moves_it_to_Nudge()
    {
        await TakenTo(TourStep.Breakdown);
        await _session.BreakdownOpenedAsync();
        Assert.That(Step, Is.EqualTo(TourStep.Nudge));
    }

    [Test]
    public async Task Explain_pressed_moves_it_to_Nudge_too()
    {
        await TakenTo(TourStep.Breakdown);
        await _session.ToggleExplainAsync();
        Assert.That(Step, Is.EqualTo(TourStep.Nudge));
    }

    [Test]
    public async Task Nudged_it_moves_to_keeping_it()
    {
        await TakenTo(TourStep.Nudge);
        await _session.NudgeAsync(new AvoidNudge(ParliamentHill));
        Assert.That(Step, Is.EqualTo(TourStep.Keep));
    }

    [Test]
    public async Task Exported_it_s_done()
    {
        await TakenTo(TourStep.Keep);
        await _session.GpxAsync();
        Assert.That(Step, Is.EqualTo(TourStep.Done));
    }

    [Test]
    public async Task Exported_for_a_watch_it_s_done()
    {
        await TakenTo(TourStep.Keep);
        await _session.TcxAsync();
        Assert.That(Step, Is.EqualTo(TourStep.Done));
    }

    [Test]
    public async Task Sent_to_the_phone_it_s_done()
    {
        await TakenTo(TourStep.Keep);
        await _session.PhoneAsync();
        Assert.That(Step, Is.EqualTo(TourStep.Done));
    }

    [Test]
    public async Task Saved_it_s_done()
    {
        await TakenTo(TourStep.Keep);
        await _session.SaveAsync("First Loop");
        Assert.That(Step, Is.EqualTo(TourStep.Done));
    }

    //  Done out of turn, a step isn't: the tour stays where it is.
    [Test]
    public async Task Nudged_before_its_turn_it_stays_where_it_is()
    {
        await TakenTo(TourStep.Page);
        await _session.NudgeAsync(new AvoidNudge(ParliamentHill));
        Assert.That(Step, Is.EqualTo(TourStep.Page));
    }

    [Test]
    public async Task A_step_skipped_it_moves_on()
    {
        await TakenTo(TourStep.Page);
        await _session.SkipTourStepAsync();
        Assert.That(Step, Is.EqualTo(TourStep.Breakdown));
    }

    [Test]
    public async Task Ended_there_s_no_tour()
    {
        await TakenTo(TourStep.Page);
        await _session.EndTourAsync();
        Assert.That(_session.Tour, Is.Null);
    }

    [Test]
    public async Task Taken_again_it_starts_again()
    {
        await TakenTo(TourStep.Page);
        await _session.TakeTourAsync();
        Assert.That(Step, Is.EqualTo(TourStep.Start));
    }

    //  A Point-to-point set last time: the tour is of a Loop.
    [Test]
    public async Task Taken_with_a_finish_set_it_plans_a_Loop()
    {
        await Open(new FakeLastRequest(new RequestSettings(null, 10, 0) { Finish = ParliamentHill }));
        await _session.TakeTourAsync();
        Assert.That(_session.EndElsewhere, Is.False);
    }

    //  No part of the history: Undo steps back the Loop, not the tour.
    [Test]
    public async Task Undone_the_tour_stays_where_it_is()
    {
        await TakenTo(TourStep.Keep);
        await _session.UndoAsync();
        Assert.That(Step, Is.EqualTo(TourStep.Keep));
    }

    //  Each step says what to do, and which control it's about.
    [Test]
    public async Task Each_step_says_what_to_do_and_where()
    {
        await _session.TakeTourAsync();
        var said = new List<(string, string?)>();
        for (var i = 0; i < FirstLoopTour.Steps; i++)
        {
            said.Add((_session.Tour!.Words, _session.Tour.Here));
            await _session.SkipTourStepAsync();
        }
        Assert.That((said.Select(s => s.Item1).Distinct().Count(), said.Select(s => s.Item2).Distinct().Count(), said.All(s => s.Item1.Length > 0 && s.Item2 is not null), _session.Tour!.Step), Is.EqualTo((6, 6, true, TourStep.Done)));
    }
}
