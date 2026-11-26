using Bunit;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Components;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The planner page (#150, T146b): a Loop over Map gaps has them drawn with it (not told on its card, T196),
/// for the user to check each is passable; and a possible one near it, clicked, includes it this time (T146c).
/// </summary>
public class ShowingMapGaps : PlannerPageSpecs
{
    //  T196 (#200; user: "I don't think we need that extra line about 'a gap in the loop' because the icon shows there's a
    //  gap"): drawn on the route, its yellow dot, whatever the Gaps in OSM overlay is set to; not said on the card too.
    [Test]
    public void A_Map_gap_is_drawn_with_the_route_and_not_told_on_the_card()
    {
        var gapped = Second with { MapGaps = [new MapGapCrossed(KenwoodHouse, ParliamentHill, 3.4)] };
        _planner.Imported = gapped;
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(gapped)));
        Assert.That(screen.Find(".pager").TextContent, Does.Not.Contain("gap in the loop"));
    }

    //  T146c: "Include this time" on a possible gap is a Nudge, the Loop through it, undone like any other.
    [Test]
    public void Including_a_possible_gap_asks_for_the_Loop_through_it_and_Undo_takes_it_back()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Second, 0);
        var screen = OfferThree();
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.GapIncluded(51.5655, -0.1641)).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        Assert.That(_planner.Leaning.Nudges.Single().Nudge, Is.EqualTo(new IncludeNudge(new LatLon(51.5655, -0.1641))));
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned)));
    }
}
