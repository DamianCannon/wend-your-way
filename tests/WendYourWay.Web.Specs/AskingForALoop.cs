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

/// <summary>The planner page (T119, from AskingForALoop): asking for a Loop: a start, a Distance, Go, and what comes back or why nothing does.</summary>
public class AskingForALoop : PlannerPageSpecs
{
    //  T096 (STORY-044 AC2): a Loop added in another direction says it's below the usual bar.
    private IRenderedComponent<Planner> OfferTwoAndOneInAnotherDirection()
    {
        _planner.Answers = [Planned, Second, Third with { InAnotherDirection = true }];
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"], Is.Not.Empty));
        return screen;
    }

    [Test]
    public void Before_any_Request_the_start_is_home()   // R1.1a
    {
        var screen = Open(KenwoodHouse);
        Assert.That(StartText(screen), Is.EqualTo("Home"));
        screen.WaitForAssertion(() =>
            Assert.That(_map.Invocations["showStart"].Single().Arguments[0], Is.EqualTo(KenwoodHouse)));
    }

    //  T117 (user: "we're seeing the current map tiles but *you* don't see it - that's a bit annoying
    //  and confusing"): the date of the Network's data, at the foot of the Request panel.
    [Test]
    public void The_date_of_the_maps_paths_is_shown()
    {
        _networkDate.At = new DateTimeOffset(2026, 11, 9, 21, 22, 17, TimeSpan.Zero);
        var screen = Open(KenwoodHouse);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".paths-as-of").TextContent.Trim(),
            Is.EqualTo("Paths as of 9 Nov 2026, from OpenStreetMap.")));
    }

    [Test]
    public void With_no_date_kept_none_is_shown() =>
        Assert.That(Open(KenwoodHouse).FindAll(".paths-as-of"), Is.Empty);

    //  T115 (user: "when you click on start/finish the contents should auto-select because I will be
    //  looking to replace it"): both boxes are marked for js/select-on-focus.js, which selects all
    //  they hold when they're focused.
    [TestCase(".start-search input")]
    [TestCase(".finish-search input")]
    public void Start_and_Finish_select_all_they_hold_when_clicked_into(string box) =>
        Assert.That(Open(KenwoodHouse).Find(box).HasAttribute("data-select-on-focus"), Is.True);

    [Test]
    public void Clicking_the_map_chooses_the_start()
    {
        var screen = Open(KenwoodHouse);
        ClickTheMap(screen, ParliamentHill);
        Assert.That(StartText(screen), Is.EqualTo("Chosen on the map"));
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showStart"][^1].Arguments[0], Is.EqualTo(ParliamentHill)));
    }

    [Test]
    public void Go_asks_for_a_Loop_of_the_Distance_from_the_start()
    {
        var screen = Open(KenwoodHouse);
        SetDistance(screen, 8);
        PressGo(screen);
        Assert.That(_planner.Asked, Is.EqualTo(new[] { (KenwoodHouse, 8.0) }));
    }

    [Test]
    public void A_start_clicked_on_the_map_is_asked_for_at_the_Distance()
    {
        var screen = Open(KenwoodHouse);
        SetDistance(screen, 8);
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That(_planner.Asked, Is.EqualTo(new[] { (ParliamentHill, 8.0) }));
    }

    [Test]
    public void Go_asks_for_a_Loop_leaning_as_the_Hills_slider_is_set()
    {
        var screen = Open(KenwoodHouse);
        screen.Find("input.hills").Input(0.5);
        PressGo(screen);
        Assert.That(_planner.LeansAsked, Is.EqualTo(new[] { new Leans(Hills: 0.5) }));
    }

    [Test]
    public void The_Hills_slider_starts_in_the_middle()
    {
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        Assert.That(_planner.LeansAsked, Is.EqualTo(new[] { Leans.Centre }));
    }

    [Test]
    public void The_Loop_that_comes_back_is_drawn_on_the_map()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"].Single().Arguments[0], Is.EqualTo(Planned)));
    }

    [Test]
    public void The_pager_card_shows_the_Loop()   // R10.5
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .summary").TextContent, Is.EqualTo("1 of 1 · 9.7 km")));
    }

    [Test]
    public void The_pager_card_opens_into_the_Breakdown()   // AC4, R10.6
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForElement(".pager .expand").Click();
        Assert.That(
            screen.FindAll(".breakdown tr").Select(row => row.TextContent.Trim()),
            Is.EqualTo(new[] { "Length9.71 km", "Climbing143 m", "Off-road62%", "Traffic-free17%",
                "Longest green2.40 km", "Main Road0.12 km", "Greenery54%", "Waterside31%", "Crossings7 · 2 of Main Roads" }));
    }

    //  T068: a Gravel Ride's Unconfirmed Access in its Breakdown, flagged; none, no row.
    [Test]
    public void A_Loop_on_Unconfirmed_Access_says_how_much()
    {
        _planner.Answer = Planned with { Breakdown = Planned.Breakdown with { UnconfirmedM = 1240 } };
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForElement(".pager .expand").Click();
        Assert.That(screen.Find(".breakdown tr.unconfirmed").TextContent.Trim(), Is.EqualTo("Unconfirmed access1.24 km"));
    }

    //  T069: a Gravel Ride's Obstacles in its Breakdown, by kind, most first; none, no row.
    [Test]
    public void A_Loop_with_Obstacles_lists_them()
    {
        _planner.Answer = Planned with
        {
            Breakdown = Planned.Breakdown with
            {
                Obstacles = [new(WendYourWay.Core.Network.ObstacleKind.Gate, 4), new(WendYourWay.Core.Network.ObstacleKind.KissingGate, 1)],
            },
        };
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForElement(".pager .expand").Click();
        Assert.That(screen.Find(".breakdown tr.obstacles").TextContent.Trim(), Is.EqualTo("Obstacles4 gates · 1 kissing gate"));
    }

    [Test]
    public void A_Loop_without_Obstacles_has_no_row_for_them()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForElement(".pager .expand").Click();
        Assert.That(screen.FindAll(".breakdown tr.obstacles"), Is.Empty);
    }

    [Test]
    public void A_Loop_in_another_direction_says_it_is_below_the_usual_bar()
    {
        var screen = OfferTwoAndOneInAnotherDirection();
        screen.Find(".pager .page[aria-label='Previous Loop']").Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .other-way").TextContent, Does.Contain("another direction").And.Contain("below the usual bar")));
    }

    [Test]
    public void A_Loop_on_the_usual_bar_says_nothing_of_it() =>
        Assert.That(OfferTwoAndOneInAnotherDirection().FindAll(".pager .other-way"), Is.Empty);

    //  "If I just click on the map to place the start then the floating panel just says 'No Loops
    //  yet' and none appear ... can the location search box update to show the clicked
    //  postcode?" (user, 2026-11-03).
    [Test]
    public void Clicking_the_map_finds_Loops_from_there()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That(_planner.Asked[0].Start, Is.EqualTo(ParliamentHill));
    }

    [Test]
    public void Clicking_the_map_puts_its_postcode_in_the_search_box()
    {
        _places.Nearest = BoxHillPostcode;
        var screen = Open(KenwoodHouse);
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForState(() => screen.Find(".place-search input").GetAttribute("value") == "RH5 6BY", Settling);
        Assert.That(_places.AskedNearest, Is.EqualTo(new[] { ParliamentHill }));
    }

    [Test]
    public void Clicking_the_map_with_no_postcode_near_says_it_was_chosen_on_the_map()
    {
        _places.Answer = [Dorking];
        var screen = Open(KenwoodHouse);
        Type(screen, "dor");
        screen.WaitForState(() => screen.FindAll(".place-search li").Count == 1, Settling);
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForState(() => StartText(screen) == "Chosen on the map", Settling);
        Assert.That(screen.FindAll(".place-search li"), Is.Empty);
    }

    [Test]
    public void When_nothing_is_found_it_says_so()
    {
        var screen = Open(KenwoodHouse);
        Type(screen, "zzq");
        screen.WaitForState(() => screen.FindAll(".place-search .none").Count == 1, Settling);
        Assert.That(screen.Find(".place-search .none").TextContent.Trim(), Is.EqualTo("No place or postcode of that name in England or Wales."));
    }

    [Test]
    public void When_no_Loop_can_be_made_the_pager_card_says_so()   // R1.5
    {
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager").TextContent, Does.Contain("No Loop found")));
    }

    [Test]
    public void While_a_Loop_is_being_found_Go_waits()
    {
        _planner.Pending = new TaskCompletionSource<PlannedLoop?>();
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        Assert.Multiple(() =>
        {
            Assert.That(screen.Find(".pager").TextContent.Trim(), Is.EqualTo("Finding a Loop…"));
            Assert.That(screen.Find("button.go").HasAttribute("disabled"), Is.True);
        });
        _planner.Pending.SetResult(Planned);
        screen.WaitForAssertion(() => Assert.That(screen.Find("button.go").HasAttribute("disabled"), Is.False));
    }

    [Test]
    public void Choosing_a_new_start_clears_the_last_Loop()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForElement(".pager .summary");
        var cleared = _map.Invocations["clearLoop"].Count;
        _planner.Pending = new TaskCompletionSource<PlannedLoop?>();
        ClickTheMap(screen, ParliamentHill);
        Assert.That(screen.Find(".pager").TextContent.Trim(), Is.EqualTo("Finding a Loop…"));
        screen.WaitForState(() => _map.Invocations["clearLoop"].Count > cleared, Settling);
    }

    //  A Loop found for the old start must not appear under the new one (Slice 1 review).
    [Test]
    public void Choosing_a_new_start_while_a_Loop_is_being_found_drops_that_Loop()
    {
        var old = _planner.Pending = new TaskCompletionSource<PlannedLoop?>();
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        //  The new start's Loops are still being found when the old start's come.
        _planner.Pending = new TaskCompletionSource<PlannedLoop?>();
        ClickTheMap(screen, ParliamentHill);
        old.SetResult(Planned);
        Assert.Multiple(() =>
        {
            Assert.That(_planner.Tokens[0].IsCancellationRequested, Is.True);
            Assert.That(screen.Find(".pager").TextContent.Trim(), Is.EqualTo("Finding a Loop…"));
            Assert.That(_map.Invocations["showLoop"], Is.Empty);
        });
    }

    [Test]
    public void When_a_Loop_can_t_be_planned_the_pager_card_says_so_and_Go_works_again()
    {
        _planner.Failure = new InvalidOperationException("the database has gone away");
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(screen.Find(".pager").TextContent, Does.Contain("Couldn't plan a Loop"));
            Assert.That(screen.Find("button.go").HasAttribute("disabled"), Is.False);
        }));
    }

    [Test]
    public void A_start_outside_England_and_Wales_is_refused_with_a_message_saying_so()   // STORY-009 AC3, R1.2
    {
        _planner.Failure = new OutsideEnglandAndWalesException();
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForAssertion(() =>
            Assert.That(screen.Find(".pager").TextContent, Does.Contain("outside England and Wales")));
    }

    [TestCase(0.5)]
    [TestCase(1000)]
    public void A_Distance_outside_1_to_100_km_is_not_asked_for(double km)
    {
        var screen = Open(KenwoodHouse);
        SetDistance(screen, km);
        PressGo(screen);
        Assert.Multiple(() =>
        {
            Assert.That(_planner.Asked, Is.Empty);
            Assert.That(screen.Find(".pager").TextContent, Does.Contain("from 1 to 100 km"));
        });
    }
}
