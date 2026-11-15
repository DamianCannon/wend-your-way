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

/// <summary>The planner page (T119, from AskingForALoop): car parks around here: listed, marked, compared, chosen as the start, and named on the Loop card.</summary>
public class StartingFromACarPark : PlannerPageSpecs
{
    private object?[] StartsShown() => [.. _map.Invocations["showStarts"][^1].Arguments];

    //  T061: the Loop card names the car park started from, opened, where a long name can wrap;
    //  the bar stays short ("maybe without the name or shown only in the expanded view so that it
    //  can word wrap", user, 2026-11-03).
    private IRenderedComponent<Planner> StartAtSheenGate()
    {
        _carParks.Answer = new([SheenGate, Pond, Wimbledon], [SheenGate, Wimbledon]);
        _planner.Answers = [Planned, Second];
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => screen.FindAll(".starts button").Count == 2, Settling);
        screen.FindAll(".starts button")[0].Click();
        screen.WaitForState(() => screen.FindAll(".pager .summary").Count > 0, Settling);
        return screen;
    }

    private static string Preview(IRenderedComponent<Planner> screen, int i) => screen.FindAll(".starts button")[i].QuerySelector(".preview")?.TextContent.Trim() ?? "";

    //  T062: compare before you commit. Loop 1 from each of the first five places listed, one at a
    //  time, its length, green share and climbing beside it.
    private static readonly FoundStart[] Six =
        [.. Enumerable.Range(0, 6).Select(i => new FoundStart($"Gate {i}", new LatLon(51.44 + (i * 0.001), -0.27), 338_000 - i))];

    //  T066: car park details, only what's known.
    private static readonly FoundStart PenPonds = Pond with
    {
        Details = new WendYourWay.Core.Network.CarParkDetails(WendYourWay.Core.Network.CarParkFee.Free, 120, Toilets: true, Cafe: true),
    };

    private static string[] Listed(IRenderedComponent<Planner> screen) =>
        [.. screen.FindAll(".starts button").Select(b => System.Text.RegularExpressions.Regex.Replace(b.TextContent, @"\s+", " ").Trim())];

    [Test]
    public void The_car_parks_around_the_map_in_view_are_listed_best_first()
    {
        _carParks.Answer = new([SheenGate, Pond, Wimbledon], [SheenGate, Wimbledon]);
        var screen = Open(KenwoodHouse);
        SetDistance(screen, 12);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => screen.FindAll(".starts button").Count == 2, Settling);
        Assert.Multiple(() =>
        {
            Assert.That((_carParks.Asked[^1].View, _carParks.Asked[^1].DistanceKm), Is.EqualTo((Richmond, 12.0)));
            Assert.That(screen.Find(".where .around .heading").TextContent.Trim(), Is.EqualTo("Car parks around here"));
            Assert.That(Listed(screen), Is.EqualTo(new[] { "Sheen Gate 338 km of paths", "Wimbledon Common 126 km of paths" }));
        });
    }

    [Test]
    public void Every_car_park_in_view_is_marked_on_the_map()
    {
        _carParks.Answer = new([SheenGate, Pond, Wimbledon], [SheenGate, Wimbledon]);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => _map.Invocations["showStarts"].Count > 0, Settling);
        Assert.That(StartsShown()[0], Is.EqualTo(new[]
        {
            new StartOnMap(SheenGate.At, "Sheen Gate · 338 km of paths"),
            new StartOnMap(Pond.At, "Pen Ponds · 338 km of paths"),
            new StartOnMap(Wimbledon.At, "Wimbledon Common · 126 km of paths"),
        }));
    }

    [Test]
    public void Choosing_a_car_park_around_here_makes_it_the_start_and_finds_its_Loops()
    {
        _carParks.Answer = new([SheenGate, Pond, Wimbledon], [SheenGate, Wimbledon]);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => screen.FindAll(".starts button").Count == 2, Settling);
        screen.FindAll(".starts button")[1].Click();
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That((_planner.Asked[0].Start, StartText(screen)), Is.EqualTo((Wimbledon.At, "Wimbledon Common")));
    }

    [Test]
    public void A_car_park_listed_says_what_s_known_of_it()
    {
        _carParks.Answer = new([PenPonds, SheenGate], [PenPonds, SheenGate]);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => screen.FindAll(".starts button").Count == 2, Settling);
        Assert.Multiple(() =>
        {
            Assert.That(screen.FindAll(".starts button")[0].QuerySelector(".details")?.TextContent.Trim(), Is.EqualTo("Free · 120 spaces · Toilets · Café"));
            Assert.That(screen.FindAll(".starts button")[1].QuerySelector(".details"), Is.Null);
        });
    }

    [Test]
    public void A_car_parks_marker_says_what_s_known_of_it()
    {
        _carParks.Answer = new([PenPonds, SheenGate], [PenPonds, SheenGate]);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => _map.Invocations["showStarts"].Count > 0, Settling);
        Assert.That(StartsShown()[0], Is.EqualTo(new[]
        {
            new StartOnMap(PenPonds.At, "Pen Ponds · 338 km of paths · Free · 120 spaces · Toilets · Café"),
            new StartOnMap(SheenGate.At, "Sheen Gate · 338 km of paths"),
        }));
    }

    [Test]
    public void The_first_five_places_listed_are_compared_in_turn()
    {
        _carParks.Answer = new(Six, Six);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        SpinWait.SpinUntil(() => _previews.Asked.Count >= 5, Settling);
        Thread.Sleep(200);
        Assert.That(_previews.Asked, Is.EqualTo(Six.Take(5).Select(p => (p.At, 10.0, Leans.Centre))));
    }

    [Test]
    public void Each_place_compared_shows_its_Loop_1()
    {
        _carParks.Answer = new(Six, Six);
        _previews.Answers[Six[0].At] = new LoopPreview(10_640, 97.2, 121.4);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => Preview(screen, 0) is not ("" or "Finding a Loop…") && Preview(screen, 1) is not ("" or "Finding a Loop…"), Settling);
        Assert.That((Preview(screen, 0), Preview(screen, 1)), Is.EqualTo(("10.6 km · 97% green · 121 m up", "No Loop")));
    }

    [Test]
    public void While_a_place_is_worked_out_it_says_so()
    {
        _carParks.Answer = new(Six, Six);
        _previews.Gate = new TaskCompletionSource();
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => Preview(screen, 0) != "", Settling);
        Assert.That((Preview(screen, 0), Preview(screen, 1)), Is.EqualTo(("Finding a Loop…", "")));
    }

    [Test]
    public void Moving_the_map_stops_the_comparing()
    {
        _carParks.Answer = new(Six, Six);
        _previews.Gate = new TaskCompletionSource();
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        SpinWait.SpinUntil(() => _previews.Tokens.Count > 0, Settling);
        ViewTheMap(screen, Richmond with { North = 51.47 });
        Assert.That(_previews.Tokens[0].IsCancellationRequested, Is.True);
    }

    [Test]
    public void A_place_compared_is_kept_for_the_same_Distance_and_Leans()
    {
        _carParks.Answer = new(Six, Six);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        SpinWait.SpinUntil(() => _previews.Asked.Count >= 5, Settling);
        ViewTheMap(screen, Richmond with { North = 51.47 });
        Thread.Sleep(1000);
        Assert.That(_previews.Asked, Has.Count.EqualTo(5));
    }

    [Test]
    public void A_new_Distance_compares_them_again()
    {
        _carParks.Answer = new(Six, Six);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        SpinWait.SpinUntil(() => _previews.Asked.Count >= 5, Settling);
        SetDistance(screen, 20);
        SpinWait.SpinUntil(() => _previews.Asked.Count >= 10, Settling);
        Assert.That(_previews.Asked.Skip(5).Select(a => a.DistanceKm), Is.All.EqualTo(20));
    }

    [Test]
    public void A_Request_stops_the_comparing()
    {
        _carParks.Answer = new(Six, Six);
        _previews.Gate = new TaskCompletionSource();
        _planner.Pending = new TaskCompletionSource<PlannedLoop?>();
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        SpinWait.SpinUntil(() => _previews.Tokens.Count > 0, Settling);
        PressGo(screen);
        Assert.That(_previews.Tokens[0].IsCancellationRequested, Is.True);
    }

    [Test]
    public void The_Loop_card_opened_names_the_car_park_started_from()
    {
        var screen = StartAtSheenGate();
        screen.Find(".pager button.expand").Click();
        Assert.That(screen.Find(".pager .place").TextContent.Trim(), Is.EqualTo("From Sheen Gate"));
    }

    [Test]
    public void The_Loop_cards_bar_leaves_the_name_out() =>
        Assert.That(Pager(StartAtSheenGate()), Is.EqualTo("1 of 2 · 9.7 km"));

    [Test]
    public void The_Loop_card_closed_doesn_t_name_it() =>
        Assert.That(StartAtSheenGate().FindAll(".pager .place"), Is.Empty);

    [Test]
    public void A_start_chosen_on_the_map_isn_t_named_on_the_Loop_card()
    {
        _carParks.Answer = new([SheenGate], [SheenGate]);
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => screen.FindAll(".starts button").Count == 1, Settling);
        screen.FindAll(".starts button")[0].Click();
        screen.WaitForState(() => screen.FindAll(".pager .summary").Count > 0, Settling);
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForState(() => _planner.Asked.Count == 2 && screen.FindAll(".pager .summary").Count > 0, Settling);
        Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 9.7 km"));
    }

    [Test]
    public void Clicking_a_car_park_marked_on_the_map_makes_it_the_start()
    {
        _carParks.Answer = new([SheenGate, Pond, Wimbledon], [SheenGate, Wimbledon]);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => _map.Invocations["showStarts"].Count > 0, Settling);
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.StartClicked(1)).GetAwaiter().GetResult();
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That(_planner.Asked[0].Start, Is.EqualTo(Pond.At));
    }

    [Test]
    public void Zoomed_out_it_says_to_zoom_in()
    {
        _carParks.Answer = new([SheenGate], [SheenGate]);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => screen.FindAll(".starts button").Count == 1, Settling);
        ViewTheMap(screen, Richmond, zoom: 9);
        screen.WaitForState(() => screen.FindAll(".starts .none").Count == 1, Settling);
        Assert.Multiple(() =>
        {
            Assert.That(screen.Find(".starts .none").TextContent.Trim(), Is.EqualTo("Zoom in to see car parks."));
            Assert.That(_map.Invocations["clearStarts"], Is.Not.Empty);
        });
    }

    [Test]
    public void A_new_Distance_ranks_the_car_parks_around_here_again()
    {
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        SetDistance(screen, 20);
        screen.WaitForState(() => _carParks.Asked.Count == 2, Settling);
        Assert.That(_carParks.Asked[^1].DistanceKm, Is.EqualTo(20));
    }

    [Test]
    public void The_car_park_chosen_is_marked_as_chosen_on_the_map()
    {
        _carParks.Answer = new([SheenGate, Pond, Wimbledon], [SheenGate, Wimbledon]);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => screen.FindAll(".starts button").Count == 2, Settling);
        screen.FindAll(".starts button")[1].Click();
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That(StartsShown()[1], Is.EqualTo(2));
    }

    //  "Maybe we don't need the 'drive up to' options any more since actually we can just select
    //  from the car parks shown on the map" (user, 2026-11-02).
    [Test]
    public void There_is_no_drive_to_choose() =>
        Assert.That(Open(KenwoodHouse).FindAll("select.longest-drive, button.find-start"), Is.Empty);
}
