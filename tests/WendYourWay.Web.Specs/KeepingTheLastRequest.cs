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

/// <summary>The planner page (T119, from AskingForALoop): the last Request's settings kept, and the planner opened on them.</summary>
public class KeepingTheLastRequest : PlannerPageSpecs
{
    [Test]
    public void The_planner_opens_on_the_last_Requests_Distance_and_Hills()
    {
        var screen = OpenAfter(new RequestSettings(ParliamentHill, 12, 0.5));
        screen.WaitForState(() => screen.Find("input.distance").GetAttribute("value") == "12", Settling);
        Assert.That(screen.Find("input.hills").GetAttribute("value"), Is.EqualTo("0.5"));
    }

    [Test]
    public void The_planner_opens_on_the_last_Requests_start()
    {
        var screen = OpenAfter(new RequestSettings(ParliamentHill, 12, 0.5));
        screen.WaitForState(() => StartText(screen) == "Chosen on the map", Settling);
        screen.WaitForState(() => _map.Invocations["showStart"].Any(c => Equals(c.Arguments[0], ParliamentHill)), Settling);
    }

    [Test]
    public void A_last_Request_from_home_opens_at_home()
    {
        var screen = OpenAfter(new RequestSettings(null, 12, 0.5));
        screen.WaitForState(() => screen.Find("input.distance").GetAttribute("value") == "12", Settling);
        Assert.That(StartText(screen), Is.EqualTo("Home"));
    }

    [Test]
    public void Go_keeps_the_Requests_settings_with_home_as_home()
    {
        var screen = Open(KenwoodHouse);
        SetDistance(screen, 12);
        PressGo(screen);
        Assert.That(_last.Saved, Is.EqualTo(new RequestSettings(null, 12, 0)));
    }

    [Test]
    public void Go_keeps_a_start_chosen_on_the_map()
    {
        var screen = Open(KenwoodHouse);
        ClickTheMap(screen, ParliamentHill);
        PressGo(screen);
        Assert.That(_last.Saved, Is.EqualTo(new RequestSettings(ParliamentHill, 10, 0)));
    }

    //  T058: the Off-road slider, tarmac to trails, as the Hills one.
    [Test]
    public void Go_asks_for_a_Loop_leaning_as_both_sliders_are_set()
    {
        var screen = Open(KenwoodHouse);
        screen.Find("input.hills").Input(-0.5);
        screen.Find("input.off-road").Input(1);
        PressGo(screen);
        Assert.That(_planner.LeansAsked, Is.EqualTo(new[] { new Leans(Hills: -0.5, OffRoad: 1) }));
    }

    [Test]
    public void Moving_the_Off_road_slider_re_ranks_the_Loops_offered_at_its_Lean()
    {
        var screen = OfferThree();
        screen.Find("input.off-road").Input(0.5);
        screen.WaitForState(() => _planner.Leaning.ReRanks.Count > 0, Settling);
        Assert.That(_planner.Leaning.ReRanks, Is.EqualTo(new[] { new Leans(OffRoad: 0.5) }));
    }

    [Test]
    public void The_planner_opens_on_the_last_Requests_Off_road_Lean()
    {
        var screen = OpenAfter(new RequestSettings(null, 12, 0, OffRoadLean: -1));
        screen.WaitForState(() => screen.Find("input.off-road").GetAttribute("value") == "-1", Settling);
    }

    [Test]
    public void Go_keeps_the_Off_road_Lean()
    {
        var screen = Open(KenwoodHouse);
        screen.Find("input.off-road").Input(0.5);
        PressGo(screen);
        Assert.That(_last.Saved?.OffRoadLean, Is.EqualTo(0.5));
    }

    [Test]
    public void Moving_the_Hills_slider_keeps_its_Lean()
    {
        var screen = OfferThree();
        Slide(screen, -0.5);
        screen.WaitForState(() => _last.Saved?.HillsLean == -0.5, Settling);
    }
}
