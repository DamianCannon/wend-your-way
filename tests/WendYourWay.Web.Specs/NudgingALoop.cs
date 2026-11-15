using Bunit;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Places;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Web.Components;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Data.Planning;
using WendYourWay.Data.Places;
using WendYourWay.Web.Planning;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace WendYourWay.Web.Specs;

/// <summary>The planner page (T119, from AskingForALoop): Nudging a Loop: dragged, avoided, a stop or an Avoid removed, a Segment Blocked; the map kept where it is.</summary>
public class NudgingALoop : PlannerPageSpecs
{
    //  T087: the map fitted to a route only when a different route is shown; a Nudge, or Undo of
    //  one, redraws it where the map is ("it should remember my zoom", user, 2026-11-08).
    private bool LastDrawFitted() => (bool)_map.Invocations["showLoop"][^1].Arguments[1]!;

    // How many Marks the map was last asked to draw.
    private int DrawnMarks() => ((IEnumerable<MarkOnMap>)_map.Invocations["showMarks"][^1].Arguments[0]!).Count();

    //  T082: Blocked Marks. "Never use this" on the route keeps that Segment off every Loop from
    //  now on, and the Loop shown goes round it at once; Blocked Segments drawn on the map, and
    //  unblocked from there.
    private static void BlockAt(IRenderedComponent<Planner> screen, LatLon at) =>
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.BlockClicked(at.Latitude, at.Longitude)).GetAwaiter().GetResult();

    private static void AvoidAt(IRenderedComponent<Planner> screen, LatLon at) =>
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.AvoidClicked(at.Latitude, at.Longitude)).GetAwaiter().GetResult();

    [Test]
    public void Dragging_the_route_goes_via_where_it_s_dropped()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThree();
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That(_planner.Leaning.Nudges.Single(), Is.EqualTo((Planned, (LoopNudge)new ViaNudge(Grabbed, Dropped))));
        Assert.That(Pager(screen), Is.EqualTo("1 of 3 · 10.2 km"));
    }

    [Test]
    public void Avoid_this_takes_that_bit_off_the_Loop()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThree();
        AvoidAt(screen, Grabbed);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That(_planner.Leaning.Nudges.Single().Nudge, Is.EqualTo(new AvoidNudge(Grabbed)));
    }

    [Test]
    public void Undo_puts_the_Loop_back_a_Nudge_at_a_time()
    {
        _planner.Leaning.Nudged = (loop, _) => new NudgedLoop(loop == Planned ? Second : Third, 0);
        var screen = OfferThree();
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Third)));
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned)));
        Assert.That(Greyed(screen, "undo"), Is.True);
    }

    [Test]
    public void A_Loop_not_nudged_has_no_Undo() =>
        Assert.That(OfferThree().FindAll(".pager .undo"), Is.Empty);

    [Test]
    public void A_Loop_nudged_out_of_its_window_says_by_how_much()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 2.1);
        var screen = OfferThree();
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .nudge-note").TextContent.Trim(), Is.EqualTo("2.1 km longer than you asked")));
    }

    [Test]
    public void A_Nudge_with_no_way_leaves_the_Loop_and_says_so()
    {
        var screen = OfferThree();
        AvoidAt(screen, Grabbed);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .nudge-note").TextContent.Trim(), Is.EqualTo("No way round there: the Loop is as it was")));
        Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned));
    }

    //  The page asks for the next Loop's own Nudges: paging away and back keeps a Loop nudged.
    [Test]
    public void A_nudged_Loop_stays_nudged_while_paging()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Third, 0);
        var screen = OfferThree();
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Third)));
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.Find(".pager .page[aria-label='Previous Loop']").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Third)));
    }

    [Test]
    public void Mark_Blocked_keeps_the_Segment_off_every_Loop_and_goes_round_it_now()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThree();
        BlockAt(screen, Grabbed);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That((_marks.Kept.Single().Segment, _planner.Leaning.Nudges.Single().Nudge), Is.EqualTo((FakeOffer.SegmentThere, (LoopNudge)new AvoidNudge(Grabbed))));
        Assert.That(screen.Find(".pager .nudge-note").TextContent.Trim(), Is.EqualTo("Blocked: no Loop will use it"));
    }

    [Test]
    public void A_Blocked_Segment_is_drawn_on_the_map()
    {
        var screen = OfferThree();
        BlockAt(screen, Grabbed);
        screen.WaitForAssertion(() => Assert.That(DrawnMarks(), Is.EqualTo(1)));
    }

    [Test]
    public void The_Marks_are_drawn_when_the_planner_opens()
    {
        _marks.Kept.Add(new Mark(7, new WendYourWay.Core.Network.SegmentKey(5, 6, 7), [KenwoodHouse, ParliamentHill]));
        var screen = Open(KenwoodHouse);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showMarks"], Is.Not.Empty));
        Assert.That(((IEnumerable<MarkOnMap>)_map.Invocations["showMarks"][^1].Arguments[0]!).Single(), Is.EqualTo(new MarkOnMap(7, [KenwoodHouse, ParliamentHill])).Using<MarkOnMap>(
            (a, b) => a.Id == b.Id && a.Line.SequenceEqual(b.Line)));
    }

    [Test]
    public void Unblock_takes_the_Mark_away()
    {
        _marks.Kept.Add(new Mark(7, new WendYourWay.Core.Network.SegmentKey(5, 6, 7), [KenwoodHouse, ParliamentHill]));
        var screen = Open(KenwoodHouse);
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.UnblockClicked(7)).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(DrawnMarks(), Is.Zero));
        Assert.That(_marks.Kept, Is.Empty);
    }

    [Test]
    public void A_Request_s_Loop_is_fitted_to_the_map()
    {
        var screen = OfferThree();
        Assert.That(LastDrawFitted(), Is.True);
    }

    [Test]
    public void A_Nudge_keeps_the_map_where_it_is()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThree();
        AvoidAt(screen, Grabbed);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That(LastDrawFitted(), Is.False);
    }

    [Test]
    public void Undo_of_a_Nudge_keeps_the_map_where_it_is()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThree();
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned)));
        Assert.That(LastDrawFitted(), Is.False);
    }

    [Test]
    public void Undo_of_a_Request_fits_the_Loop_before()
    {
        var screen = OfferThree();
        _planner.Answers = [Third];
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 9.3 km")));
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned)));
        Assert.That(LastDrawFitted(), Is.True);
    }

    //  T079: a stop or an Avoid removed from the map is a Nudge, undone like any other.
    [Test]
    public void Removing_a_stop_asks_for_it_removed()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThree();
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.StopRemoved(1)).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That(_planner.Leaning.Nudges.Single().Nudge, Is.EqualTo(new RemoveStopNudge(1)));
    }

    [Test]
    public void Removing_an_Avoid_asks_for_it_removed_and_Undo_puts_it_back()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThree();
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.AvoidRemoved(0)).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That(_planner.Leaning.Nudges.Single().Nudge, Is.EqualTo(new RemoveAvoidNudge(0)));
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned)));
    }

    [Test]
    public void A_removal_with_no_way_round_says_so()
    {
        var screen = OfferThree();
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.StopRemoved(0)).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .nudge-note").TextContent.Trim(), Is.EqualTo("No way without it: the Loop is as it was")));
    }
}
