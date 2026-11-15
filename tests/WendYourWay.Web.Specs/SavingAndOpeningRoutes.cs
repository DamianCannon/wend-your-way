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

/// <summary>The planner page (T119, from AskingForALoop): saving a route and opening it again from Saved Routes: its name, stars, map changes, renaming and deleting.</summary>
public class SavingAndOpeningRoutes : PlannerPageSpecs
{
    //  T085: a Saved Route reopened is nudged as an offered Loop is, once its slice is read
    //  behind it; a Nudge before then waits; if the map has changed under it, it says so.
    private IRenderedComponent<Planner> ReopenTheSavedRoute()
    {
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .open").Click();
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 9.7 km")));
        return screen;
    }

    [Test]
    public void Saving_keeps_the_Loop_shown_by_the_name_given()
    {
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        screen.WaitForAssertion(() => Assert.That(_saved.Kept.Select(r => (r.Name, r.Loop)), Is.EqualTo(new[] { ("Heath wiggle", Planned) })));
        Assert.That((_saved.Kept[0].Traversals, screen.Find(".pager .nudge-note").TextContent.Trim()),
            Is.EqualTo((FakeOffer.Traversals, "Saved as Heath wiggle")));
    }

    [Test]
    public void The_name_offered_is_where_it_starts_and_its_km()
    {
        var screen = OfferThree();
        screen.Find(".pager .save").Click();
        Assert.That(screen.Find(".pager .save-name").GetAttribute("value"), Is.EqualTo("Home · 9.7 km"));
    }

    [Test]
    public void A_nudged_Loop_is_saved_as_nudged()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThree();
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        SaveAs(screen, "Nudged");
        screen.WaitForAssertion(() => Assert.That(_saved.Kept.Single().Loop, Is.EqualTo(Second)));
    }

    [Test]
    public void Saved_Routes_lists_them_newest_first()
    {
        var screen = OfferThree();
        SaveAs(screen, "First");
        SaveAs(screen, "Second");
        OpenSavedRoutes(screen);
        screen.WaitForAssertion(() => Assert.That(screen.FindAll(".saved-routes li .name").Select(n => n.TextContent.Trim()), Is.EqualTo(new[] { "Second", "First" })));
        Assert.That((screen.Find(".saved-routes li .km").TextContent.Trim(), screen.Find(".saved-routes li .date").TextContent.Trim()),
            Is.EqualTo(("9.7 km", "7 Nov 2026")));
    }

    //  T097 (STORY-045 AC2): a Saved Route keeps the app's score for it, and shows its stars.
    //  Its stars a Run's (T108): 0.3 + 0.380 x 0.5 off-road + 0.0132 x 10 m a km = 0.622, 4 stars.
    [Test]
    public void Saving_keeps_the_app_s_score_for_the_Loop()
    {
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        screen.WaitForAssertion(() => Assert.That(_saved.Scores, Is.EqualTo(new Dictionary<int, RouteScore?> { [1] = FakeOffer.Score })));
    }

    [Test]
    public void Saved_Routes_shows_the_app_s_stars_for_each()
    {
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        OpenSavedRoutes(screen);
        var stars = screen.WaitForElement(".saved-routes li .app-stars");
        Assert.That((stars.TextContent.Trim(), stars.GetAttribute("aria-label")), Is.EqualTo(("App ★★★★☆", "The app's stars: 4 of 5")));
    }

    //  T097 (STORY-045 AC3): a Saved Route the user rates 2 stars from the app's stands out.
    [Test]
    public void A_Saved_Route_rated_against_the_app_stands_out()
    {
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .my-stars button");
        screen.FindAll(".saved-routes li .my-stars button")[1].Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".saved-routes li").ClassList.Contains("disagree"), Is.True));
    }

    //  T103 (STORY-016 AC2): one the map has changed under says so in Saved Routes; the others don't.
    [Test]
    public void A_Saved_Route_the_map_has_changed_under_says_so()
    {
        var changed = _saved.SaveAsync("Changed", Planned, FakeOffer.Traversals).GetAwaiter().GetResult();
        _saved.SaveAsync("Same", Planned, FakeOffer.Traversals).GetAwaiter().GetResult();
        _saved.MapChanged.Add(changed);
        var screen = OfferThree();
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .name");
        Assert.That(screen.FindAll(".saved-routes li").Select(li => li.QuerySelector(".map-changed")?.TextContent.Trim()),
            Is.EqualTo(new[] { null, SavedRouteSummary.MapChangedNote }));
    }

    //  T136 (user: "I will want to be able to nudge these in due course"): moved onto the map as it is.
    private IRenderedComponent<Planner> SavedRoutesWithOneChanged()
    {
        var changed = _saved.SaveAsync("Changed", Planned, FakeOffer.Traversals).GetAwaiter().GetResult();
        _saved.SaveAsync("Same", Planned, FakeOffer.Traversals).GetAwaiter().GetResult();
        _saved.MapChanged.Add(changed);
        var screen = OfferThree();
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .name");
        return screen;
    }

    [Test]
    public void Only_one_the_map_has_changed_under_offers_to_move_it() =>
        Assert.That(SavedRoutesWithOneChanged().FindAll(".saved-routes li").Select(li => li.QuerySelector("button.move")?.TextContent.Trim()),
            Is.EqualTo(new[] { null, "Move to the new map" }));

    [Test]
    public void Moved_it_says_nothing_more_of_the_map()
    {
        var screen = SavedRoutesWithOneChanged();
        screen.Find(".saved-routes button.move").Click();
        screen.WaitForAssertion(() => Assert.That((_screen.RouteMoves.Asked.Count, screen.FindAll(".saved-routes .map-changed").Count), Is.EqualTo((1, 0))));
    }

    [Test]
    public void Kept_as_it_was_it_says_where_it_no_longer_follows_the_map()
    {
        _screen.RouteMoves.Kept = new RouteMove(RouteMoveOutcome.Kept, "From 3.2 km it no longer follows the map.");
        var screen = SavedRoutesWithOneChanged();
        screen.Find(".saved-routes button.move").Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".saved-routes .map-changed").TextContent.Trim(),
            Is.EqualTo(SavedRouteSummary.MapChangedNote + " From 3.2 km it no longer follows the map.")));
    }

    [Test]
    public void A_Saved_Route_saved_before_the_stars_says_so()
    {
        _saved.SaveAsync("Old", Planned, FakeOffer.Traversals).GetAwaiter().GetResult();
        var screen = OfferThree();
        OpenSavedRoutes(screen);
        var stars = screen.WaitForElement(".saved-routes li .app-stars");
        Assert.That((stars.TextContent.Trim(), stars.GetAttribute("aria-label")),
            Is.EqualTo(("App saved before the stars", "Saved before the app's stars: wend outings score scores it")));
    }

    //  Opened from Saved Routes and saved again, it has the app's stars: the route opened again scores it.
    [Test]
    public void A_Saved_Route_opened_and_saved_again_keeps_the_app_s_score()
    {
        _saved.SaveAsync("Old", Planned, FakeOffer.Traversals).GetAwaiter().GetResult();
        var screen = OfferThree();
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .open").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned)));
        SaveAs(screen, "Old again");
        screen.WaitForAssertion(() => Assert.That(_saved.Scores.GetValueOrDefault(2), Is.EqualTo(FakeOffer.Score)));
    }

    //  T097 (STORY-045 AC1): the user gives a Saved Route their own stars.
    [Test]
    public void Clicking_a_star_gives_a_Saved_Route_that_many()
    {
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .my-stars button");
        screen.FindAll(".saved-routes li .my-stars button")[4].Click();
        screen.WaitForAssertion(() => Assert.That(_saved.Rated, Is.EqualTo(new Dictionary<int, int?> { [1] = 5 })));
        Assert.That(screen.FindAll(".saved-routes li .my-stars button").Select(b => b.GetAttribute("aria-pressed")), Is.All.EqualTo("true"));
    }

    //  A long name is cut short, and named in full on pointing (user, 2026-11-08).
    [Test]
    public void A_Saved_Route_s_full_name_shows_on_pointing()
    {
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        OpenSavedRoutes(screen);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".saved-routes li .name").GetAttribute("title"), Is.EqualTo("Heath wiggle")));
    }

    [Test]
    public void A_Saved_Route_opens_on_the_map()
    {
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        _planner.Answers = [Third];
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 9.3 km")));
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .open").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned)));
        Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 9.7 km"));
    }

    //  T137 (user: "when looking at a saved route I don't want clicks on the map to set a new start
    //  point for a route because that's not the point of this view"), as with the Outings open.
    [Test]
    public void Clicking_the_map_with_the_Saved_Routes_open_sets_no_start()
    {
        var screen = ReopenTheSavedRoute();
        var starts = _map.Invocations.Count(i => i.Identifier == "showStart");
        ClickTheMap(screen, ParliamentHill);
        Assert.That((_map.Invocations.Count(i => i.Identifier == "showStart"), Pager(screen)), Is.EqualTo((starts, "1 of 1 · 9.7 km")));
    }

    [Test]
    public void A_Saved_Route_is_deleted_only_when_you_say_yes()
    {
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .delete").Click();
        Assert.That(_saved.Kept, Has.Count.EqualTo(1));
        screen.Find(".saved-routes li .confirm-delete").Click();
        screen.WaitForAssertion(() => Assert.That((_saved.Kept.Count, screen.FindAll(".saved-routes li").Count), Is.EqualTo((0, 0))));
    }

    [Test]
    public void A_Saved_Route_can_be_renamed()
    {
        var screen = OfferThree();
        SaveAs(screen, "Heath wiggle");
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .rename").Click();
        screen.Find(".saved-routes li .new-name").Input("Thames Path out and back");
        screen.Find(".saved-routes li form").Submit();
        screen.WaitForAssertion(() => Assert.That(_saved.Kept.Single().Name, Is.EqualTo("Thames Path out and back")));
    }

    [Test]
    public void A_Saved_Route_reopened_is_nudged()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = ReopenTheSavedRoute();
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That((_planner.Reopened.Single().Saved, _planner.Reopened.Single().Activity), Is.EqualTo((Planned, Activity.Run)));
    }

    [Test]
    public void A_Nudge_before_it_s_ready_waits_for_it()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        _planner.ReopenPending = new TaskCompletionSource();
        var screen = ReopenTheSavedRoute();
        //  Not waited for: it waits for the Saved Route to be ready.
        var dragging = screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.RouteDragged(Grabbed.Latitude, Grabbed.Longitude, Dropped.Latitude, Dropped.Longitude));
        Assert.That((dragging.IsCompleted, _map.Invocations["showLoop"][^1].Arguments[0]), Is.EqualTo((false, (object?)Planned)));
        screen.InvokeAsync(() => _planner.ReopenPending.SetResult()).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
    }

    [Test]
    public void One_the_map_has_changed_under_says_so_when_nudged()
    {
        _planner.ReopenFailure = new RouteNotMatchedException(LoopPlanner.MapChanged);
        var screen = ReopenTheSavedRoute();
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .nudge-note").TextContent.Trim(), Is.EqualTo(LoopPlanner.MapChanged)));
    }

    //  Nudged and saved again: a new Saved Route, by the Segments it now has.
    [Test]
    public void Reopened_nudged_and_saved_it_is_a_new_Saved_Route()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = ReopenTheSavedRoute();
        Drag(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        SaveAs(screen, "Heath wiggle, nudged");
        screen.WaitForAssertion(() => Assert.That(_saved.Kept.Select(r => (r.Name, r.Loop)), Is.EqualTo(new[] { ("Heath wiggle", Planned), ("Heath wiggle, nudged", Second) })));
    }
}
