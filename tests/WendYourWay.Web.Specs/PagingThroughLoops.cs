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

/// <summary>The planner page (T119, from AskingForALoop): paging through the Loops offered, and hiding the route to see the ground under it.</summary>
public class PagingThroughLoops : PlannerPageSpecs
{
    //  T071: one click hides the route, to see the ground under it, and one shows it again.
    private static string? RouteButton(IRenderedComponent<Planner> screen) => screen.Find(".pager .route").GetAttribute("aria-label");

    [Test]
    public void The_pager_says_which_Loop_of_how_many_and_its_length() =>
        Assert.That(Pager(OfferThree()), Is.EqualTo("1 of 3 · 9.7 km"));

    [Test]
    public void The_next_arrow_shows_the_next_Loop_alone()
    {
        var screen = OfferThree();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That(Pager(screen), Is.EqualTo("2 of 3 · 10.2 km"));
    }

    [Test]
    public void Hide_route_takes_the_route_off_the_map()
    {
        var screen = OfferThree();
        screen.Find(".pager .route").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showRoute"][^1].Arguments[0], Is.EqualTo(false)));
        Assert.That(RouteButton(screen), Is.EqualTo("Show route"));
    }

    //  T157 (#161; user: "Maybe the switch should just say 'Explain?'"): pressed, the map says the route's notes.
    [Test]
    public void Explain_says_why_the_route_goes_as_it_does()
    {
        var screen = OfferThree();
        var button = screen.Find(".pager .explain");
        Assert.That((button.TextContent.Trim(), button.GetAttribute("aria-pressed")), Is.EqualTo(("Explain?", "false")));
        button.Click();
        screen.WaitForAssertion(() => Assert.That((screen.Find(".pager .explain").GetAttribute("aria-pressed"), _map.Invocations["showNotes"].Count), Is.EqualTo(("true", 2))));
    }

    //  T164 (#168; user: "move the Explain? button after the save icon"): after the bookmark; QR, GPX and TCX under the bar (T198).
    [Test]
    public void Explain_comes_after_the_bookmark() =>
        Assert.That(OfferThree().FindAll(".pager .bar button").Select(b => b.ClassName!.Split(" ")[0]).SkipWhile(c => c != "route"),
            Is.EqualTo(new[] { "route", "save", "explain", "expand" }));

    //  T198 (#202; user: "push the download buttons (all three) to the second line so that they are grouped separately"):
    //  under the bar, after Undo and Redo.
    [Test]
    public void The_downloads_are_on_the_second_line() =>
        Assert.That(OfferThree().FindAll(".pager .nudged button").Select(b => b.ClassName!.Split(" ")[0]),
            Is.EqualTo(new[] { "undo", "redo", "phone", "export", "export" }));

    [Test]
    public void Show_route_puts_it_back()
    {
        var screen = OfferThree();
        screen.Find(".pager .route").Click();
        screen.Find(".pager .route").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showRoute"][^1].Arguments[0], Is.EqualTo(true)));
        Assert.That(RouteButton(screen), Is.EqualTo("Hide route"));
    }

    //  User, 2026-11-09: "if I've hidden a route but then the map wants to load a new route ... the
    //  route should be shown again and the toggle put back in the 'shown' state".
    [Test]
    public void Paging_to_another_Loop_shows_a_hidden_route_again()
    {
        var screen = OfferThree();
        screen.Find(".pager .route").Click();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That((RouteButton(screen), _map.Invocations["showRoute"][^1].Arguments[0]), Is.EqualTo(((string?)"Hide route", (object?)true)));
    }

    [Test]
    public void A_new_Request_shows_a_hidden_route_again()
    {
        var screen = OfferThree();
        screen.Find(".pager .route").Click();
        var asked = _planner.Asked.Count;
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForState(() => _planner.Asked.Count > asked && !screen.Markup.Contains("Finding a Loop", StringComparison.Ordinal), Settling);
        screen.WaitForAssertion(() => Assert.That((RouteButton(screen), _map.Invocations["showRoute"][^1].Arguments[0]), Is.EqualTo(((string?)"Hide route", (object?)true))));
    }

    [Test]
    public void The_previous_arrow_goes_back()
    {
        var screen = OfferThree();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.Find(".pager .page[aria-label='Previous Loop']").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned)));
    }

    //  "The left/right arrows should wrap around so I can go right from loop 5 back to loop 1
    //  directly" (user, 2026-10-29).
    [Test]
    public void From_the_last_Loop_next_goes_round_to_the_first()
    {
        var screen = OfferThree();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        Assert.That(Pager(screen), Is.EqualTo("1 of 3 · 9.7 km"));
    }

    [Test]
    public void From_the_first_Loop_previous_goes_round_to_the_last()
    {
        var screen = OfferThree();
        screen.Find(".pager .page[aria-label='Previous Loop']").Click();
        Assert.That(Pager(screen), Is.EqualTo("3 of 3 · 9.3 km"));
    }

    [Test]
    public void The_left_arrow_key_goes_round_too()
    {
        var screen = OfferThree();
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.PageKey(-1)).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("3 of 3 · 9.3 km")));
    }

    [Test]
    public void With_one_Loop_there_is_nowhere_to_go()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForState(() => screen.FindAll(".pager .summary").Count > 0, Settling);
        Assert.That(screen.FindAll(".pager .page").Select(b => b.HasAttribute("disabled")), Is.All.True);
    }

    //  R10.5: the left and right arrow keys page too (the map passes them on).
    [Test]
    public void The_right_and_left_arrow_keys_page_through_the_Loops()
    {
        var screen = OfferThree();
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.PageKey(1)).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("2 of 3 · 10.2 km")));
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.PageKey(-1)).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 3 · 9.7 km")));
    }
}
