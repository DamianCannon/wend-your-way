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

/// <summary>The planner page (T119, from AskingForALoop): Undo and Redo through Requests and Nudges alike, three Requests back.</summary>
public class UndoingAndRedoing : PlannerPageSpecs
{
    private IRenderedComponent<Planner> OfferThreeThenStartAgain()
    {
        var screen = OfferThree();
        _planner.Answers = [Third];
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 9.3 km")));
        return screen;
    }

    private static void RedoKey(IRenderedComponent<Planner> screen) =>
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.RedoKey()).GetAwaiter().GetResult();

    private static void UndoKey(IRenderedComponent<Planner> screen) =>
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.UndoKey()).GetAwaiter().GetResult();

    [Test]
    public void Undo_after_a_new_start_brings_back_the_Loops_before()
    {
        var screen = OfferThreeThenStartAgain();
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 3 · 9.7 km")));
        Assert.That((_map.Invocations["showLoop"][^1].Arguments[0], _map.Invocations["showStart"][^1].Arguments[0]), Is.EqualTo(((object)Planned, (object)KenwoodHouse)));
    }

    [Test]
    public void Ctrl_Z_undoes_and_Ctrl_Y_redoes()
    {
        var screen = OfferThreeThenStartAgain();
        UndoKey(screen);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 3 · 9.7 km")));
        RedoKey(screen);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 9.3 km")));
        Assert.That(_map.Invocations["showStart"][^1].Arguments[0], Is.EqualTo(ParliamentHill));
    }

    [Test]
    public void Redo_is_greyed_out_till_there_s_something_to_redo()
    {
        var screen = OfferThreeThenStartAgain();
        Assert.That((Greyed(screen, "undo"), Greyed(screen, "redo")), Is.EqualTo((false, true)));
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That((Greyed(screen, "undo"), Greyed(screen, "redo")), Is.EqualTo((true, false))));
    }

    //  Undone Nudges and Requests alike, back to the first Loops.
    [Test]
    public void Undo_steps_back_through_Nudges_and_Requests_alike()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThreeThenStartAgain();
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        UndoKey(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Third)));
        UndoKey(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned)));
        Assert.That(Greyed(screen, "undo"), Is.True);
    }

    //  Something new after an Undo: what could be redone is gone.
    [Test]
    public void A_Nudge_after_an_Undo_drops_the_Redo()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThreeThenStartAgain();
        UndoKey(screen);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 3 · 9.7 km")));
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That(Greyed(screen, "redo"), Is.True);
    }

    //  The stray click still finding its Loops: Undo stops it and brings back those before.
    [Test]
    public void Undo_while_a_new_start_s_Loops_are_found_brings_back_those_before()
    {
        var screen = OfferThree();
        _planner.Pending = new TaskCompletionSource<PlannedLoop?>();
        ClickTheMap(screen, ParliamentHill);
        UndoKey(screen);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 3 · 9.7 km")));
        Assert.That((_planner.Cancelled, _map.Invocations["showStart"][^1].Arguments[0]), Is.EqualTo((true, (object)KenwoodHouse)));
    }

    //  The last 3 Requests are kept, to be nudged again: no further back.
    [Test]
    public void Undo_goes_back_no_more_than_3_Requests()
    {
        var screen = OfferThree();
        foreach (var at in new[] { ParliamentHill, Richmond.Middle, KenwoodHouse })
        {
            var asked = _planner.Asked.Count;
            ClickTheMap(screen, at);
            screen.WaitForState(() => _planner.Asked.Count > asked && !screen.Markup.Contains("Finding a Loop", StringComparison.Ordinal), Settling);
        }
        for (var i = 0; i < 3; i++) UndoKey(screen);
        screen.WaitForAssertion(() => Assert.That(Greyed(screen, "undo"), Is.True));
        Assert.That(_map.Invocations["showStart"][^1].Arguments[0], Is.EqualTo(ParliamentHill));
    }
}
