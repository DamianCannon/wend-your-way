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

/// <summary>The planner page (T119, from AskingForALoop): searching for a place or a postcode, and choosing one.</summary>
public class SearchingForAPlace : PlannerPageSpecs
{
    private static string[] Suggested(IRenderedComponent<Planner> screen) =>
        [.. screen.FindAll(".place-search li").Select(li => System.Text.RegularExpressions.Regex.Replace(li.TextContent, @"\s+", " ").Trim())];

    [Test]
    public void Typing_a_name_searches_round_the_middle_of_the_map()
    {
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        Type(screen, "dor");
        SpinWait.SpinUntil(() => _places.Asked.Count > 0, Settling);
        Assert.That(_places.Asked, Is.EqualTo(new[] { ("dor", Richmond.Middle) }));
    }

    [Test]
    public void One_character_searches_for_nothing()
    {
        var screen = Open(KenwoodHouse);
        Type(screen, "d");
        Thread.Sleep(500);
        Assert.That(_places.Asked, Is.Empty);
    }

    [Test]
    public void Only_what_s_typed_last_is_searched_for()
    {
        var screen = Open(KenwoodHouse);
        Type(screen, "do");
        Type(screen, "dor");
        Type(screen, "dork");
        SpinWait.SpinUntil(() => _places.Asked.Count > 0, Settling);
        Thread.Sleep(400);
        Assert.That(_places.Asked.Select(a => a.Text), Is.EqualTo(new[] { "dork" }));
    }

    [Test]
    public void The_places_found_say_what_and_where_they_are()
    {
        _places.Answer = [Dorking, BoxHillPostcode];
        var screen = Open(KenwoodHouse);
        Type(screen, "dor");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 2, Settling);
        Assert.That(Suggested(screen), Is.EqualTo(new[] { "Dorking Town · Mole Valley", "RH5 6BY Postcode · Box Hill" }));
    }

    [Test]
    public void The_places_found_credit_OS_Royal_Mail_and_National_Statistics()
    {
        _places.Answer = [Dorking];
        var screen = Open(KenwoodHouse);
        Type(screen, "dor");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 1, Settling);
        Assert.That(screen.Find(".place-search .credit").TextContent, Does.Contain("OS data").And.Contain("Royal Mail").And.Contain("National Statistics"));
    }

    [Test]
    public void Choosing_a_place_takes_the_map_there_at_its_zoom()
    {
        _places.Answer = [Dorking, BoxHillPostcode];
        var screen = Open(KenwoodHouse);
        Type(screen, "rh5");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 2, Settling);
        screen.FindAll(".place-search li")[1].Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["goTo"][^1].Arguments, Is.EqualTo(new object[] { BoxHillPostcode.At, 16 })));
    }

    //  T093: in the Start box, a place chosen is the start, not only where the map goes.
    [Test]
    public void Choosing_a_place_makes_it_the_start()
    {
        _places.Answer = [Dorking];
        var screen = Open(KenwoodHouse);
        Type(screen, "dor");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 1, Settling);
        screen.FindAll(".place-search li")[0].Click();
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That((_planner.Asked[0].Start, StartText(screen)), Is.EqualTo((Dorking.At, Dorking.Name)));
    }

    [Test]
    public void Choosing_a_place_puts_its_name_in_the_box_and_closes_the_list()
    {
        _places.Answer = [Dorking];
        var screen = Open(KenwoodHouse);
        Type(screen, "dor");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 1, Settling);
        screen.FindAll(".place-search li")[0].Click();
        //  The choice is handled asynchronously, the map moved first: wait for the box to show it.
        screen.WaitForAssertion(() =>
            Assert.That((screen.Find(".place-search input").GetAttribute("value"), screen.FindAll(".place-search li").Count), Is.EqualTo(("Dorking", 0))), Settling);
    }

    [Test]
    public void Enter_chooses_the_first_place_found()
    {
        _places.Answer = [Dorking, BoxHillPostcode];
        var screen = Open(KenwoodHouse);
        Type(screen, "dor");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 2, Settling);
        screen.Find(".place-search input").KeyDown("Enter");
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["goTo"][^1].Arguments[0], Is.EqualTo(Dorking.At)));
    }

    [Test]
    public void The_down_arrow_and_Enter_choose_the_next()
    {
        _places.Answer = [Dorking, BoxHillPostcode];
        var screen = Open(KenwoodHouse);
        Type(screen, "dor");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 2, Settling);
        screen.Find(".place-search input").KeyDown("ArrowDown");
        screen.Find(".place-search input").KeyDown("ArrowDown");
        screen.Find(".place-search input").KeyDown("Enter");
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["goTo"][^1].Arguments[0], Is.EqualTo(BoxHillPostcode.At)));
    }

    [Test]
    public void Escape_closes_the_list()
    {
        _places.Answer = [Dorking];
        var screen = Open(KenwoodHouse);
        Type(screen, "dor");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 1, Settling);
        screen.Find(".place-search input").KeyDown("Escape");
        // Waited for, not asserted at once: about one run in six still saw the list (2026-11-07),
        // its cause not found.
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 0, Settling);
    }

    //  "System.ObjectDisposedException: The CancellationTokenSource has been disposed" (user,
    //  2026-11-03): a search typed past, finishing after the next began.
    [Test]
    public void A_search_typed_past_that_finishes_late_changes_nothing()
    {
        _places.Answer = [Dorking, BoxHillPostcode];
        _places.HoldFirst = new TaskCompletionSource();
        var screen = Open(KenwoodHouse);
        Type(screen, "dor");
        SpinWait.SpinUntil(() => _places.Asked.Count == 1, Settling);
        Type(screen, "dork");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 2, Settling);
        _places.HoldFirst.SetResult();
        Thread.Sleep(300);
        Assert.That((screen.FindAll(".place-search li").Count, screen.FindAll(".place-search .none").Count), Is.EqualTo((2, 0)));
    }

    //  "If I put in a postcode then that's pretty exact and I want to see routes from that postcode as
    //  if I had clicked that location whereas if I put in a more general location like southend on
    //  sea then sure I just want to be taken there to see car parks" (user, 2026-11-03).
    [Test]
    public void Choosing_a_postcode_makes_it_the_start_and_finds_its_Loops()
    {
        _places.Answer = [BoxHillPostcode];
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        Type(screen, "rh5 6by");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 1, Settling);
        screen.FindAll(".place-search li")[0].Click();
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.Multiple(() =>
        {
            Assert.That(_planner.Asked[0].Start, Is.EqualTo(BoxHillPostcode.At));
            Assert.That(StartText(screen), Is.EqualTo("RH5 6BY"));
        });
    }

    [Test]
    public void Choosing_a_postcode_district_only_takes_the_map_there()
    {
        _places.Answer = [new FoundPlace("RH5", PlaceKind.District, "Box Hill", BoxHillPostcode.At)];
        var screen = Open(KenwoodHouse);
        Type(screen, "rh5");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 1, Settling);
        screen.FindAll(".place-search li")[0].Click();
        Thread.Sleep(300);
        Assert.That((StartText(screen), _planner.Asked.Count), Is.EqualTo(("Home", 0)));
    }
}
