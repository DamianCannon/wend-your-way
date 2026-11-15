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

/// <summary>The planner page (T119, from AskingForALoop): the Leans re-ranking the Loops offered, and the search at a Lean once the slider stays still.</summary>
public class ReRankingTheLoops : PlannerPageSpecs
{
    [Test]
    public void Moving_the_Hills_slider_re_ranks_the_Loops_offered_without_a_new_Request()
    {
        _planner.Leaning.ReRanked = leans => leans.Hills == 1 ? [Third, Second] : null;
        var screen = OfferThree();
        Slide(screen, 1);
        screen.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(_planner.Leaning.ReRanks, Is.EqualTo(new[] { new Leans(Hills: 1) }));
            Assert.That(_planner.Asked, Has.Count.EqualTo(1));
            Assert.That(Pager(screen), Is.EqualTo("1 of 2 · 9.3 km"));
            Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Third));
        }));
    }

    [Test]
    //  "When the reorder is finished it's not loading the new loop 1 for the map" (user,
    //  2026-10-27): the map shows the Loop now in the place shown, even if the old one is still offered.
    public void Re_ranked_Loop_1_is_shown_even_if_the_Loop_shown_is_still_offered()
    {
        _planner.Leaning.ReRanked = leans => leans.Hills == 1 ? [Third, Planned] : null;
        var screen = OfferThree();
        Slide(screen, 1);
        screen.WaitForState(() => Pager(screen) == "1 of 2 · 9.3 km", Settling);
        Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Third));
    }

    //  "When you change the sliders the selection flips back to loop 1 which actually is
    //  distracting" (user, 2026-10-27): the place paged to stays, showing the Loop now there.
    [Test]
    public void After_paging_moving_a_slider_keeps_the_place_paged_to()
    {
        _planner.Leaning.ReRanked = leans => leans.Hills == 1 ? [Third, Second] : null;
        var screen = OfferThree();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        Slide(screen, 1);
        screen.WaitForState(() => Pager(screen) == "2 of 2 · 10.2 km", Settling);
        Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second));
    }

    [Test]
    public void A_place_paged_to_that_s_no_longer_offered_becomes_the_last_Loop()
    {
        _planner.Leaning.ReRanked = leans => leans.Hills == 1 ? [Third, Second] : null;
        var screen = OfferThree();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        Slide(screen, 1);
        screen.WaitForState(() => Pager(screen) == "2 of 2 · 10.2 km", Settling);
    }

    [Test]
    public void When_the_background_search_ends_its_Loop_1_is_shown()
    {
        _planner.Leaning.ReRanked = leans => leans.Hills == 1 ? [Third, Planned] : null;
        _planner.Leaning.Searched = leans => leans.Hills == 1 ? [Second, Third] : null;
        var screen = OfferThree();
        Slide(screen, 1);
        screen.WaitForState(() => Pager(screen) == "1 of 2 · 10.2 km", Settling);
        //  The map is drawn just after the pager card.
        SpinWait.SpinUntil(() => Equals(_map.Invocations["showLoop"][^1].Arguments[0], Second), Settling);
        Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second));
    }

    //  A Loop paged to since the slider moved stays shown while the search is still finding.
    [Test]
    public void A_Loop_paged_to_during_the_background_search_stays_shown_after_it()
    {
        _planner.Leaning.ReRanked = leans => leans.Hills == 1 ? [Third, Planned] : null;
        _planner.Leaning.Searched = leans => leans.Hills == 1 ? [Second, Planned] : null;
        _planner.Leaning.SearchGate = new TaskCompletionSource();
        var screen = OfferThree();
        Slide(screen, 1);
        UntilSearched();
        screen.WaitForState(() => Pager(screen) == "1 of 2 · 9.3 km", Settling);
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        _planner.Leaning.SearchGate.SetResult();
        screen.WaitForState(() => screen.FindAll(".pager .searching, .pager .progress").Count == 0, Settling);
        Assert.That(Pager(screen), Is.EqualTo("2 of 2 · 9.7 km"));
    }

    [Test]
    public void Before_any_Loops_are_offered_moving_the_Hills_slider_asks_for_nothing()
    {
        var screen = Open(KenwoodHouse);
        Slide(screen, 1);
        Assert.Multiple(() =>
        {
            Assert.That(_planner.Leaning.ReRanks, Is.Empty);
            Assert.That(_planner.Asked, Is.Empty);
        });
    }

    //  The Loops come back at the Lean Go asked for: if the slider moved meanwhile, they're
    //  re-ranked to where it is now.
    [Test]
    public void Moved_while_Loops_are_being_found_they_re_re_ranked_to_it_when_found()
    {
        _planner.Pending = new TaskCompletionSource<PlannedLoop?>();
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        Slide(screen, -1);
        _planner.Pending.SetResult(Planned);
        screen.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(_planner.LeansAsked, Is.EqualTo(new[] { Leans.Centre }));
            Assert.That(_planner.Leaning.ReRanks, Is.EqualTo(new[] { new Leans(Hills: -1) }));
        }));
    }

    [Test]
    public void Once_the_slider_stays_still_the_Loops_are_searched_at_its_Lean()
    {
        _planner.Leaning.Searched = leans => leans.Hills == 1 ? [Second, Third] : null;
        var screen = OfferThree();
        Slide(screen, 1);
        // Waited for without asserting: NUnit counts a failed assertion even when a retry passes.
        screen.WaitForState(() => _planner.Leaning.Searches.Count > 0 && Pager(screen) != "1 of 3 · 9.7 km", Settling);
        Assert.Multiple(() =>
        {
            Assert.That(_planner.Leaning.Searches, Is.EqualTo(new[] { new Leans(Hills: 1) }));
            Assert.That(Pager(screen), Is.EqualTo("1 of 2 · 10.2 km"));
        });
    }

    [Test]
    public void Moved_again_before_it_stays_still_only_the_last_Lean_is_searched()
    {
        var screen = OfferThree();
        Slide(screen, 1);
        Slide(screen, -1);
        UntilSearched();
        Assert.That(_planner.Leaning.Searches, Is.EqualTo(new[] { new Leans(Hills: -1) }));
    }

    [Test]
    public void While_it_searches_the_pager_card_says_it_s_looking_for_more()
    {
        _planner.Leaning.SearchGate = new TaskCompletionSource();
        var screen = OfferThree();
        Slide(screen, 1);
        UntilSearched();
        Assert.That(screen.Find(".pager .searching").TextContent.Trim(), Is.EqualTo("Looking for more Loops…"));
        _planner.Leaning.SearchGate.SetResult();
        screen.WaitForState(() => screen.FindAll(".pager .searching").Count == 0, Settling);
    }

    [Test]
    public void Choosing_a_new_start_while_it_searches_drops_what_it_finds()
    {
        _planner.Leaning.SearchGate = new TaskCompletionSource();
        _planner.Leaning.Searched = _ => [Third];
        var screen = OfferThree();
        Slide(screen, 1);
        UntilSearched();
        ClickTheMap(screen, ParliamentHill);
        _planner.Leaning.SearchGate.SetResult();
        screen.WaitForState(() => screen.FindAll(".pager .summary").Count > 0, Settling);
        Assert.That(screen.FindAll(".pager .searching"), Is.Empty, "still looking for more for the old start");
    }
}
