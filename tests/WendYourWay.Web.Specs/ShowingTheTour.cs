using Bunit;
using WendYourWay.Web.Components.Pages;

namespace WendYourWay.Web.Specs;

/// <summary>The first-Loop tour on the planner page (T158, #162): its card, the control each step is about, and Help.</summary>
public class ShowingTheTour : PlannerPageSpecs
{
    private static string? Here(IRenderedComponent<Planner> screen) => screen.Find(".planner").GetAttribute("data-tour");

    [Test]
    public void Opened_for_the_first_time_it_offers_the_tour() =>
        Assert.That(Open(KenwoodHouse).FindAll(".tour .show-me").Select(b => b.TextContent), Is.EqualTo(new[] { "Show me" }));

    [Test]
    public void Show_me_and_it_s_at_the_start_the_Start_box_standing_out()
    {
        var screen = Open(KenwoodHouse);
        screen.Find(".tour .show-me").Click();
        screen.WaitForAssertion(() => Assert.That((screen.Find(".tour .count").TextContent, Here(screen)), Is.EqualTo(("Your first loop · 1 of 6", "start"))));
    }

    [Test]
    public void No_thanks_and_the_card_goes()
    {
        var screen = Open(KenwoodHouse);
        screen.FindAll(".tour .quiet").Single(b => b.TextContent == "No thanks").Click();
        screen.WaitForAssertion(() => Assert.That(screen.FindAll(".tour"), Is.Empty));
    }

    //  Only Go itself moves the tour past Go.
    [Test]
    public void Go_pressed_it_moves_on_to_paging()
    {
        _planner.Answers = [Planned, Second, Third];
        var screen = Open(KenwoodHouse);
        screen.Find(".tour .show-me").Click();
        PressGo(screen);
        screen.WaitForAssertion(() => Assert.That(Here(screen), Is.EqualTo("page")));
    }

    [Test]
    public void The_card_opened_into_its_Breakdown_moves_it_on_to_Nudge()
    {
        _planner.Answers = [Planned, Second, Third];
        var screen = Open(KenwoodHouse);
        screen.Find(".tour .show-me").Click();
        PressGo(screen);
        screen.WaitForAssertion(() => Assert.That(Here(screen), Is.EqualTo("page")));
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.WaitForAssertion(() => Assert.That(Here(screen), Is.EqualTo("breakdown")));
        screen.Find(".pager .expand").Click();
        screen.WaitForAssertion(() => Assert.That(Here(screen), Is.EqualTo("nudge")));
    }

    //  Taken from Help, the Request panel opens, where a click on the map sets a start.
    [Test]
    public void Taken_from_Help_the_Request_panel_opens()
    {
        var screen = Open(KenwoodHouse);
        screen.FindAll(".tour .quiet").Single(b => b.TextContent == "No thanks").Click();
        screen.Find(".rail button[aria-label='Help']").Click();
        screen.Find(".panel .take-tour").Click();
        screen.WaitForAssertion(() => Assert.That((screen.Find(".panel h2").TextContent, Here(screen)), Is.EqualTo(("Request", "start"))));
    }
}
