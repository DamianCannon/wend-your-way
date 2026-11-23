using Bunit;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Components;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The planner page (#150, T146b): a Loop over Map gaps says so on its card, with each gap's length,
/// for the user to check it's passable; and a possible one near it, clicked, includes it this time (T146c).
/// </summary>
public class ShowingMapGaps : PlannerPageSpecs
{
    [Test]
    public void A_Map_gap_is_told_on_the_card()
    {
        _planner.Imported = Second with { MapGaps = [new MapGapCrossed(KenwoodHouse, ParliamentHill, 3.4)] };
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .map-gap-note").TextContent.Trim(), Is.EqualTo("A gap in the loop (3 m): check it's passable")));
    }

    [Test]
    public void Two_are_told_together()
    {
        _planner.Imported = Second with { MapGaps = [new MapGapCrossed(KenwoodHouse, ParliamentHill, 3.4), new MapGapCrossed(ParliamentHill, KenwoodHouse, 0.6)] };
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .map-gap-note").TextContent.Trim(), Is.EqualTo("2 gaps in the loop (3 m, 1 m): check they're passable")));
    }

    [Test]
    public void A_Loop_with_none_tells_none()
    {
        var screen = OfferThree();
        Assert.That(screen.FindAll(".pager .map-gap-note"), Is.Empty);
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
