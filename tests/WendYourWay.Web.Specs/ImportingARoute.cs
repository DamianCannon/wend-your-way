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

/// <summary>The planner page (T119, from AskingForALoop): importing a GPX route: matched for the Activity, named, its gaps told, nudged and undone.</summary>
public class ImportingARoute : PlannerPageSpecs
{
    [Test]
    public void An_imported_GPX_is_matched_for_the_Activity_and_shown_alone()
    {
        _planner.Imported = Second;
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That((_planner.ImportsAsked.Single().Route.Count, _planner.ImportsAsked.Single().Activity, Pager(screen)),
            Is.EqualTo((3, Activity.Run, "1 of 1 · 10.2 km")));
    }

    [Test]
    public void The_name_offered_for_saving_it_is_the_GPX_s()
    {
        _planner.Imported = Second;
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        screen.Find(".pager .save").Click();
        Assert.That(screen.Find(".pager .save-name").GetAttribute("value"), Is.EqualTo("Heath from a friend"));
    }

    [Test]
    public void A_GPX_with_no_name_is_offered_its_file_s()
    {
        _planner.Imported = Second;
        var screen = OfferThree();
        Import(screen, HeathGpx.Replace("<name>Heath from a friend</name>", "", StringComparison.Ordinal), "Sunday hills.gpx");
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        screen.Find(".pager .save").Click();
        Assert.That(screen.Find(".pager .save-name").GetAttribute("value"), Is.EqualTo("Sunday hills"));
    }

    [Test]
    public void Loops_planned_after_it_are_offered_their_own_names()
    {
        _planner.Imported = Second;
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        _planner.Answers = [Third];
        screen.Find(".rail button[aria-label='Request']").Click();
        PressGo(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Third)));
        screen.Find(".pager .save").Click();
        Assert.That(screen.Find(".pager .save-name").GetAttribute("value"), Does.EndWith("9.3 km"));
    }

    //  Saved with the panel open, as an import leaves it: listed at once (found in the browser).
    [Test]
    public void A_route_saved_with_Saved_Routes_open_is_listed_at_once()
    {
        _planner.Imported = Second;
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        SaveAs(screen, "Friend's");
        screen.WaitForAssertion(() => Assert.That(screen.FindAll(".saved-routes li .name").Select(n => n.TextContent.Trim()), Is.EqualTo(new[] { "Friend's" })));
    }

    [Test]
    public void Saved_it_is_kept_by_its_own_Segments()
    {
        _planner.Imported = Second;
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        SaveAs(screen, "Friend's");
        screen.WaitForAssertion(() => Assert.That(_saved.Kept.Select(r => (r.Name, r.Loop, r.Traversals)), Is.EqualTo(new[] { ("Friend's", Second, FakeOffer.Traversals) })));
    }

    [Test]
    public void Its_gaps_are_told_on_the_card()
    {
        _planner.Imported = Second with { Gaps = [new RouteGap([KenwoodHouse, ParliamentHill], 250), new RouteGap([ParliamentHill, KenwoodHouse], 160)] };
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .gap-note").TextContent.Trim(), Is.EqualTo("2 gaps, 0.4 km not on the map")));
    }

    [Test]
    public void One_gap_is_told_as_one()
    {
        _planner.Imported = Second with { Gaps = [new RouteGap([KenwoodHouse, ParliamentHill], 90)] };
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .gap-note").TextContent.Trim(), Is.EqualTo("1 gap, 0.1 km not on the map")));
    }

    [Test]
    public void A_Loop_with_no_gaps_tells_none()
    {
        var screen = OfferThree();
        Assert.That(screen.FindAll(".pager .gap-note"), Is.Empty);
    }

    [Test]
    public void One_that_can_t_be_matched_is_refused_in_the_panel_the_Loops_shown_kept()
    {
        _planner.ImportFailure = new RouteNotMatchedException("Under half of that route follows the map.");
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".import-problem").TextContent.Trim(), Is.EqualTo("Under half of that route follows the map.")));
        Assert.That((_map.Invocations["showLoop"][^1].Arguments[0], Pager(screen)), Is.EqualTo(((object?)Planned, "1 of 3 · 9.7 km")));
    }

    [Test]
    public void A_file_that_isn_t_a_GPX_is_refused_unmatched()
    {
        var screen = OfferThree();
        Import(screen, "<kml/>", "places.kml");
        screen.WaitForAssertion(() => Assert.That(screen.Find(".import-problem").TextContent.Trim(), Is.EqualTo("That file isn't a GPX.")));
        Assert.That(_planner.ImportsAsked, Is.Empty);
    }

    [Test]
    public void Undo_after_an_import_brings_back_the_Loops_before()
    {
        _planner.Imported = Second;
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 10.2 km")));
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 3 · 9.7 km")));
    }

    [Test]
    public void An_imported_route_is_nudged_as_an_offered_Loop_is()
    {
        _planner.Imported = Second;
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Third, 0);
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Third)));
    }
}
