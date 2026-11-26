using Bunit;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Components;
using WendYourWay.Web.Components.Pages;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The planner page (T196, #200; user: "that text at the bottom shifts the content so my mouse is no longer over the tab
/// arrows which is annoying"): the Loop card is pinned at the bottom, so whatever comes and goes on it goes above its bar;
/// below it only Undo and Redo, always there, greyed out with nothing to undo or redo.
/// </summary>
public class KeepingTheArrowsStill : PlannerPageSpecs
{
    // The classes of what lies below the card's bar.
    private static string[] Below(IRenderedComponent<Planner> screen)
    {
        var below = new List<string>();
        for (var e = screen.Find(".pager .bar").NextElementSibling; e is not null; e = e.NextElementSibling) below.Add(e.ClassName ?? e.TagName);
        return [.. below];
    }

    [Test]
    public void Offered_Loops_have_only_Undo_and_Redo_below()
    {
        var screen = OfferThree();
        Assert.That(Below(screen), Is.EqualTo(new[] { "nudged" }));
    }

    [Test]
    public void A_nudge_s_note_goes_above()
    {
        var screen = OfferThree();
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.AvoidClicked(51.5655, -0.1641)).GetAwaiter().GetResult();
        screen.WaitForElement(".pager .nudge-note");
        Assert.That(Below(screen), Is.EqualTo(new[] { "nudged" }));
    }

    [Test]
    public void Naming_a_route_to_save_goes_above()
    {
        var screen = OfferThree();
        screen.Find(".pager .save").Click();
        screen.WaitForElement(".pager .save-name");
        Assert.That(Below(screen), Is.EqualTo(new[] { "nudged" }));
    }

    [Test]
    public void An_imported_route_s_gaps_go_above()
    {
        _planner.Imported = Second with { Gaps = [new RouteGap([KenwoodHouse, ParliamentHill], 300)] };
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForElement(".pager .gap-note");
        Assert.That(Below(screen), Is.EqualTo(new[] { "nudged" }));
    }
}
