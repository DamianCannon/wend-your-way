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

/// <summary>The planner page (T119, from AskingForALoop): the Breakdown of the Loop shown, and its elevation profile.</summary>
public class ReadingTheBreakdown : PlannerPageSpecs
{
    //  R3.5, STORY-010 AC5b: the elevation profile (T051). Round a Loop the ascent and descent
    //  are the same, so it gives its climbing once (user, 2026-10-25).
    private IRenderedComponent<Planner> ShowTheProfile()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForElement(".pager .expand").Click();
        return screen;
    }

    [Test]
    public void The_Breakdown_is_the_current_Loops()
    {
        var screen = OfferThree();
        screen.Find(".pager .page[aria-label='Next Loop']").Click();
        screen.WaitForElement(".pager .expand").Click();
        Assert.That(screen.FindAll(".breakdown tr")[0].TextContent.Trim(), Is.EqualTo("Length10.24 km"));
    }

    [Test]
    public void The_Breakdown_shows_the_Loops_elevation_profile_with_its_climbing()
    {
        var screen = ShowTheProfile();
        Assert.Multiple(() =>
        {
            Assert.That(screen.FindAll(".profile svg path.ground"), Has.Count.EqualTo(1));
            Assert.That(screen.Find(".profile figcaption").TextContent.Trim(), Is.EqualTo("143 m of climbing"));
        });
    }

    //  A simple axis: the lowest height at the bottom, the highest at the top, and the distance
    //  round from 0 to the Loop's length beneath.
    [Test]
    public void The_elevation_profile_is_scaled_from_its_lowest_to_its_highest_point_and_round_the_Loop()
    {
        var screen = ShowTheProfile();
        Assert.That(
            new[] { ".y-axis .bottom", ".y-axis .top", ".x-axis .start", ".x-axis .end" }.Select(label => screen.Find(".profile " + label).TextContent.Trim()),
            Is.EqualTo(new[] { "98 m", "131 m", "0 km", "9.7 km" }));
    }

    //  As Route Mapper does: pointing at the profile shows where that is on the Loop.
    [Test]
    public void Pointing_at_the_elevation_profile_marks_that_place_on_the_map()
    {
        var screen = ShowTheProfile();
        screen.FindAll(".profile rect.at")[2].MouseOver();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["markPlace"][^1].Arguments[0], Is.EqualTo(ParliamentHill)));
    }

    //  An orange dot on the profile, as on the map, with its height and distance on the axes.
    [Test]
    public void Pointing_at_the_elevation_profile_gives_the_height_and_distance_there()
    {
        var screen = ShowTheProfile();
        screen.FindAll(".profile rect.at")[2].MouseOver();
        screen.WaitForAssertion(() => Assert.That(screen.FindAll(".profile .dot"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(screen.Find(".profile .y-axis .now").TextContent.Trim(), Is.EqualTo("131 m"));
            Assert.That(screen.Find(".profile .x-axis .now").TextContent.Trim(), Is.EqualTo("6.0 km"));
        });
    }

    [Test]
    public void Leaving_the_elevation_profile_takes_the_mark_off_the_map()
    {
        var screen = ShowTheProfile();
        screen.FindAll(".profile rect.at")[2].MouseOver();
        screen.Find(".profile .plot").MouseLeave();
        Assert.Multiple(() =>
        {
            screen.WaitForAssertion(() => Assert.That(_map.Invocations["clearPlace"], Is.Not.Empty));
            Assert.That(screen.FindAll(".profile .dot, .profile .now"), Is.Empty);
        });
    }

    //  Without OS Terrain 50's tiles there are no heights to draw.
    [Test]
    public void Without_heights_there_is_no_elevation_profile()
    {
        _planner.Answer = Planned with { Profile = [] };
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForElement(".pager .expand").Click();
        Assert.That(screen.FindAll(".profile"), Is.Empty);
    }

    //  The OS OpenData licence: climbing comes from OS Terrain 50 (T049).
    [Test]
    public void The_Breakdown_credits_Ordnance_Survey_for_its_climbing()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForElement(".pager .expand").Click();
        Assert.That(screen.Find(".pager .credit").TextContent.Trim(), Is.EqualTo("Climbing contains OS data © Crown copyright and database right 2026"));
    }
}
