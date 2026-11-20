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

/// <summary>The planner page (T119, from AskingForALoop): Point-to-points: a finish chosen, routes to it, swapped, saved, opened and undone.</summary>
public class EndingElsewhere : PlannerPageSpecs
{
    //  The Finish box clicked into (T093): the next map click is the finish.
    private static void EndElsewhere(IRenderedComponent<Planner> screen) => screen.Find(".finish-search input").Focus();

    //  T091: a Point-to-point, "End at…" chosen on the Request panel, then where to finish.
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    private IRenderedComponent<Planner> OpenedFromSavedRoutes(IRenderedComponent<Planner> screen)
    {
        OpenSavedRoutes(screen);
        screen.WaitForElement(".saved-routes li .open").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(_saved.Kept[^1].Loop)));
        screen.Find(".rail button[aria-label='Request']").Click();
        return screen;
    }

    //  T092: a Point-to-point through what a Loop has. A 16.4 km route from Kenwood House to
    //  Pembroke Lodge, not back.
    private static readonly PlannedLoop ToPembrokeLodge = Planned with
    {
        Line = [KenwoodHouse, ParliamentHill, PembrokeLodge],
        Breakdown = Planned.Breakdown with { LengthM = 16_400 },
    };

    //  The start and finish swapped (T094).
    private IRenderedComponent<Planner> SwappedFromRichmondPark()
    {
        _planner.Answer = Planned;
        _places.Answer = [new FoundPlace("Richmond Park", PlaceKind.Park, "Richmond", PembrokeLodge)];
        var screen = Open(KenwoodHouse);
        screen.Find(".finish-search input").Input("richmond park");
        screen.WaitForState(() => screen.FindAll(".finish-search li").Count == 1, Settling);
        screen.FindAll(".finish-search li")[0].Click();
        screen.WaitForState(() => _planner.RoutesAsked.Count == 1, Settling);
        screen.Find("button.swap-ends").Click();
        screen.WaitForState(() => _planner.RoutesAsked.Count == 2, Settling);
        return screen;
    }

    private IRenderedComponent<Planner> RoutesTo(LatLon finish)
    {
        var screen = Open(KenwoodHouse);
        EndElsewhere(screen);
        ClickTheMap(screen, finish);
        screen.WaitForState(() => _planner.RoutesAsked.Count == 1, Settling);
        return screen;
    }

    //  What the Finish box shows (T093): empty for a Loop.
    private static string? FinishText(IRenderedComponent<Planner> screen) => screen.Find(".finish-search input").GetAttribute("value");

    [Test]
    public void Ending_elsewhere_a_map_click_is_the_finish_and_routes_to_it_are_asked_for()
    {
        var screen = RoutesTo(PembrokeLodge);
        Assert.That((_planner.RoutesAsked.Single().Start, _planner.RoutesAsked.Single().Finish, _planner.RoutesAsked.Single().DistanceKm, _planner.Asked.Count, StartText(screen)),
            Is.EqualTo((KenwoodHouse, PembrokeLodge, (double?)null, 0, "Home")));
    }

    [Test]
    public void The_finish_is_shown_on_the_map_and_named()
    {
        var screen = RoutesTo(PembrokeLodge);
        Assert.That((_map.Invocations["showFinish"][^1].Arguments[0], FinishText(screen)),
            Is.EqualTo(((object)PembrokeLodge, "Chosen on the map")));
    }

    //  The box a map click fills is marked, and nothing more said (T094): the Start's, till the Finish's is clicked into.
    [Test]
    public void The_box_a_map_click_fills_is_marked()
    {
        var screen = Open(KenwoodHouse);
        var before = (screen.FindAll(".start-search.active").Count, screen.FindAll(".finish-search.active").Count, screen.FindAll(".ends .hint").Count);
        EndElsewhere(screen);
        Assert.That((before, (screen.FindAll(".start-search.active").Count, screen.FindAll(".finish-search.active").Count, screen.FindAll(".ends .hint").Count)),
            Is.EqualTo(((1, 0, 0), (0, 1, 0))));
    }

    [Test]
    public void A_Loop_has_nothing_to_swap() =>
        Assert.That(Open(KenwoodHouse).FindAll("button.swap-ends"), Is.Empty);

    [Test]
    public void Swapped_the_routes_are_found_the_other_way()
    {
        SwappedFromRichmondPark();
        Assert.That((_planner.RoutesAsked[1].Start, _planner.RoutesAsked[1].Finish), Is.EqualTo((PembrokeLodge, KenwoodHouse)));
    }

    [Test]
    public void Swapped_each_box_shows_the_others_name()
    {
        var screen = SwappedFromRichmondPark();
        Assert.That((StartText(screen), FinishText(screen)), Is.EqualTo(("Richmond Park", "Home")));
    }

    [Test]
    public void Swapped_both_pins_move()
    {
        SwappedFromRichmondPark();
        Assert.That((_map.Invocations["showStart"][^1].Arguments[0], _map.Invocations["showFinish"][^1].Arguments[0]),
            Is.EqualTo(((object)PembrokeLodge, (object)KenwoodHouse)));
    }

    [Test]
    public void Swapped_Undo_swaps_back()
    {
        var screen = SwappedFromRichmondPark();
        screen.WaitForState(() => !Greyed(screen, "undo"), Settling);
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That((StartText(screen), FinishText(screen)), Is.EqualTo(("Home", "Richmond Park"))));
    }

    [Test]
    public void A_route_saved_is_kept_by_the_name_suggested()
    {
        _planner.Answer = ToPembrokeLodge;
        var screen = RoutesTo(PembrokeLodge);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Does.StartWith("1 of 1")));
        screen.Find(".pager .save").Click();
        screen.Find(".pager form.saving").Submit();
        screen.WaitForAssertion(() => Assert.That(_saved.Kept.Select(r => (r.Name, r.Loop)),
            Is.EqualTo(new[] { ("Home to Chosen on the map · 16.4 km", ToPembrokeLodge) })));
    }

    [Test]
    public void A_route_opened_from_Saved_Routes_ends_at_its_finish()
    {
        _saved.SaveAsync("To Richmond", ToPembrokeLodge, FakeOffer.Traversals);
        _places.Nearest = BoxHillPostcode;
        var screen = OpenedFromSavedRoutes(Open(KenwoodHouse));
        screen.WaitForAssertion(() => Assert.That((_map.Invocations["showFinish"][^1].Arguments[0], StartText(screen), FinishText(screen), Pager(screen)),
            Is.EqualTo(((object)PembrokeLodge, "Home", BoxHillPostcode.Name, "1 of 1 · 16.4 km"))));
    }

    [Test]
    public void A_route_opened_away_from_home_shows_its_starts_postcode()
    {
        _saved.SaveAsync("From Richmond", ToPembrokeLodge with { Line = [PembrokeLodge, ParliamentHill], Start = PembrokeLodge }, FakeOffer.Traversals);
        _places.Nearest = BoxHillPostcode;
        var screen = OpenedFromSavedRoutes(Open(KenwoodHouse));
        screen.WaitForAssertion(() => Assert.That((_map.Invocations["showStart"][^1].Arguments[0], StartText(screen)),
            Is.EqualTo(((object)PembrokeLodge, BoxHillPostcode.Name))));
    }

    //  No postcode found, the start's last one isn't left in its box.
    [Test]
    public void A_route_opened_with_no_postcode_near_its_start_says_so()
    {
        _saved.SaveAsync("From Richmond", ToPembrokeLodge with { Line = [PembrokeLodge, ParliamentHill], Start = PembrokeLodge }, FakeOffer.Traversals);
        _places.Nearest = BoxHillPostcode;
        var screen = Open(KenwoodHouse);
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForAssertion(() => Assert.That(StartText(screen), Is.EqualTo(BoxHillPostcode.Name)));
        _places.Nearest = null;
        OpenedFromSavedRoutes(screen);
        screen.WaitForAssertion(() => Assert.That(StartText(screen), Is.EqualTo("Chosen on the map")));
    }

    //  Undone back to it, its ends' names come back with it.
    [Test]
    public void A_route_imported_and_undone_back_to_keeps_its_ends_names()
    {
        _places.Nearest = BoxHillPostcode;
        _planner.Imported = ToPembrokeLodge;
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showFinish"], Is.Not.Empty));
        screen.Find(".rail button[aria-label='Request']").Click();
        screen.WaitForAssertion(() => Assert.That(FinishText(screen), Is.EqualTo(BoxHillPostcode.Name)));
        screen.Find(".finish-search input").Input("");
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        screen.WaitForState(() => !Greyed(screen, "undo"), Settling);
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(FinishText(screen), Is.EqualTo(BoxHillPostcode.Name)));
    }

    [Test]
    public void A_Loop_opened_from_Saved_Routes_ends_at_its_start()
    {
        _saved.SaveAsync("Heath", Planned, FakeOffer.Traversals);
        _planner.Answer = ToPembrokeLodge;
        var screen = OpenedFromSavedRoutes(RoutesTo(PembrokeLodge));
        screen.WaitForAssertion(() => Assert.That((FinishText(screen), _map.Invocations["clearFinish"].Count > 0, Pager(screen)),
            Is.EqualTo(("", true, "1 of 1 · 9.7 km"))));
    }

    [Test]
    public void An_imported_route_ends_at_its_finish()
    {
        _planner.Imported = ToPembrokeLodge;
        var screen = Open(KenwoodHouse);
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That((_map.Invocations["showFinish"][^1].Arguments[0], Pager(screen)),
            Is.EqualTo(((object)PembrokeLodge, "1 of 1 · 16.4 km"))));
    }

    [Test]
    public void Routes_undone_and_redone_come_back_with_their_finish()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        _planner.Answer = ToPembrokeLodge;
        EndElsewhere(screen);
        ClickTheMap(screen, PembrokeLodge);
        screen.WaitForState(() => _planner.RoutesAsked.Count == 1, Settling);
        screen.WaitForElement(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(FinishText(screen), Is.EqualTo("")));
        var shown = _map.Invocations["showFinish"].Count;
        screen.Find(".pager .redo").Click();
        screen.WaitForAssertion(() => Assert.That((FinishText(screen), _map.Invocations["showFinish"].Count, Pager(screen)),
            Is.EqualTo(("Chosen on the map", shown + 1, "1 of 1 · 16.4 km · +8.0 km"))));
    }

    //  T095: a car park chosen fills the box marked, as a map click does.
    [Test]
    public void A_car_park_chosen_with_the_Finish_marked_is_the_finish()
    {
        _carParks.Answer = new([SheenGate, Pond, Wimbledon], [SheenGate, Wimbledon]);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => screen.FindAll(".starts button").Count == 2, Settling);
        EndElsewhere(screen);
        screen.FindAll(".starts button")[1].Click();
        screen.WaitForState(() => _planner.RoutesAsked.Count == 1, Settling);
        Assert.That((_planner.RoutesAsked[0].Start, _planner.RoutesAsked[0].Finish, StartText(screen), FinishText(screen), _map.Invocations["showFinish"][^1].Arguments[0]),
            Is.EqualTo((KenwoodHouse, Wimbledon.At, "Home", "Wimbledon Common", (object)Wimbledon.At)));
    }

    [Test]
    public void A_car_park_marked_on_the_map_clicked_with_the_Finish_marked_is_the_finish()
    {
        _carParks.Answer = new([SheenGate, Pond, Wimbledon], [SheenGate, Wimbledon]);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => _map.Invocations["showStarts"].Count > 0, Settling);
        EndElsewhere(screen);
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.StartClicked(1)).GetAwaiter().GetResult();
        screen.WaitForState(() => _planner.RoutesAsked.Count == 1, Settling);
        Assert.That((_planner.RoutesAsked[0].Finish, FinishText(screen), _planner.Asked.Count), Is.EqualTo((Pond.At, "Pen Ponds", 0)));
    }

    [Test]
    public void A_car_park_chosen_with_a_finish_is_the_start_of_routes_to_it()
    {
        _carParks.Answer = new([SheenGate, Pond, Wimbledon], [SheenGate, Wimbledon]);
        var screen = RoutesTo(PembrokeLodge);
        screen.Find(".start-search input").Focus();
        ViewTheMap(screen, Richmond);
        screen.WaitForState(() => screen.FindAll(".starts button").Count == 2, Settling);
        screen.FindAll(".starts button")[1].Click();
        screen.WaitForState(() => _planner.RoutesAsked.Count == 2, Settling);
        Assert.That((_planner.RoutesAsked[1].Start, _planner.RoutesAsked[1].Finish, StartText(screen), _planner.Asked.Count),
            Is.EqualTo((Wimbledon.At, PembrokeLodge, "Wimbledon Common", 0)));
    }

    //  Clicked into, the Start's box is filled by map clicks again.
    [Test]
    public void The_Start_clicked_into_again_a_map_click_is_the_start()
    {
        var screen = RoutesTo(PembrokeLodge);
        screen.Find(".start-search input").Focus();
        ClickTheMap(screen, ParliamentHill);
        screen.WaitForState(() => _planner.RoutesAsked.Count == 2, Settling);
        Assert.That((_planner.RoutesAsked[1].Start, _planner.RoutesAsked[1].Finish), Is.EqualTo((ParliamentHill, PembrokeLodge)));
    }

    [Test]
    public void A_finish_clicked_on_the_map_shows_its_postcode()
    {
        _places.Nearest = BoxHillPostcode;
        var screen = RoutesTo(PembrokeLodge);
        screen.WaitForAssertion(() => Assert.That(FinishText(screen), Is.EqualTo(BoxHillPostcode.Name)));
    }

    [Test]
    public void An_empty_Finish_is_a_Loop()
    {
        var screen = Open(KenwoodHouse);
        EndElsewhere(screen);
        PressGo(screen);
        Assert.That((_planner.Asked.Count, _planner.RoutesAsked.Count, FinishText(screen)), Is.EqualTo((1, 0, "")));
    }

    //  The chequered pin clicked, "Remove the finish": a Loop again.
    [Test]
    public void The_finish_pin_removed_a_Loop_is_found()
    {
        var screen = RoutesTo(PembrokeLodge);
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.FinishRemoved()).GetAwaiter().GetResult();
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That((_map.Invocations["clearFinish"].Count > 0, FinishText(screen)), Is.EqualTo((true, "")));
    }

    [Test]
    public void Ending_elsewhere_the_Distance_may_be_left_blank()
    {
        var screen = Open(KenwoodHouse);
        Assert.That(screen.FindAll(".distance-hint"), Is.Empty);
        EndElsewhere(screen);
        ClickTheMap(screen, PembrokeLodge);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".distance-hint").TextContent, Is.EqualTo("blank: shortest to greenest")));
    }

    [Test]
    public void A_Distance_given_is_asked_for()
    {
        var screen = RoutesTo(PembrokeLodge);
        SetDistance(screen, 12);
        PressGo(screen);
        screen.WaitForState(() => _planner.RoutesAsked.Count == 2, Settling);
        Assert.That(_planner.RoutesAsked[1].DistanceKm, Is.EqualTo(12));
    }

    [Test]
    public void Ending_elsewhere_a_place_searched_for_is_the_finish()
    {
        _places.Answer = [new FoundPlace("Richmond Park", PlaceKind.Park, "Richmond", PembrokeLodge)];
        var screen = Open(KenwoodHouse);
        screen.Find(".finish-search input").Input("richmond park");
        screen.WaitForState(() => screen.FindAll(".finish-search li").Count == 1, Settling);
        screen.FindAll(".finish-search li")[0].Click();
        screen.WaitForState(() => _planner.RoutesAsked.Count == 1, Settling);
        Assert.That((_planner.RoutesAsked.Single().Finish, FinishText(screen)), Is.EqualTo((PembrokeLodge, "Richmond Park")));
    }

    [Test]
    public void The_Finish_emptied_a_Loop_is_found_and_the_finish_goes()
    {
        var screen = RoutesTo(PembrokeLodge);
        screen.Find(".finish-search input").Input("");
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That((_planner.Asked.Single().Start, _map.Invocations["clearFinish"].Count > 0, FinishText(screen)),
            Is.EqualTo((KenwoodHouse, true, "")));
    }

    [Test]
    public void The_finish_dragged_routes_are_asked_for_to_there()
    {
        var screen = RoutesTo(PembrokeLodge);
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.FinishDragged(ParliamentHill.Latitude, ParliamentHill.Longitude)).GetAwaiter().GetResult();
        screen.WaitForState(() => _planner.RoutesAsked.Count == 2, Settling);
        Assert.That(_planner.RoutesAsked[1].Finish, Is.EqualTo(ParliamentHill));
    }

    [Test]
    public void The_start_dragged_routes_are_asked_for_from_there()
    {
        var screen = RoutesTo(PembrokeLodge);
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.StartDragged(ParliamentHill.Latitude, ParliamentHill.Longitude)).GetAwaiter().GetResult();
        screen.WaitForState(() => _planner.RoutesAsked.Count == 2, Settling);
        Assert.That((_planner.RoutesAsked[1].Start, _planner.RoutesAsked[1].Finish), Is.EqualTo((ParliamentHill, PembrokeLodge)));
    }

    //  A Loop's start dragged too: its Loops from there.
    [Test]
    public void A_Loop_s_start_dragged_its_Loops_are_asked_for_from_there()
    {
        var screen = Open(KenwoodHouse);
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.StartDragged(ParliamentHill.Latitude, ParliamentHill.Longitude)).GetAwaiter().GetResult();
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That(_planner.Asked.Single().Start, Is.EqualTo(ParliamentHill));
    }

    [Test]
    public void Each_route_is_labelled_by_how_much_longer_than_the_shortest_way_it_is()
    {
        _planner.Answers = [Planned, Second];
        var screen = RoutesTo(PembrokeLodge);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 2 · 9.7 km · +1.3 km")));
    }

    //  The shortest way itself, or within 50 m of it, is said to be.
    [Test]
    public void The_shortest_route_is_labelled_so()
    {
        _planner.Answer = Planned;
        _planner.ShortestM = 9690;
        var screen = RoutesTo(PembrokeLodge);
        screen.WaitForAssertion(() => Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 9.7 km · shortest")));
    }

    [Test]
    public void A_route_refused_says_why()
    {
        _planner.RouteFailure = new PointToPointRefusedException("At least 8.5 km from the start to the end.");
        var screen = RoutesTo(PembrokeLodge);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager").TextContent, Does.Contain("At least 8.5 km from the start to the end.")));
    }

    [Test]
    public void The_finish_is_remembered_with_the_last_Request()
    {
        _places.Nearest = BoxHillPostcode;
        var screen = RoutesTo(PembrokeLodge);
        screen.WaitForAssertion(() => Assert.That((_last.Saved?.Finish, _last.Saved?.FinishDistanceKm, _last.Saved?.FinishName),
            Is.EqualTo(((LatLon?)PembrokeLodge, (double?)null, BoxHillPostcode.Name))));
    }

    [Test]
    public void Opened_again_it_ends_where_it_did()
    {
        _context.Services.AddSingleton<ILastRequest>(new FakeLastRequest(new RequestSettings(null, 10, 0) { Finish = PembrokeLodge, FinishDistanceKm = 12, FinishName = "Pembroke Lodge" }));
        var screen = Open(KenwoodHouse);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showFinish"], Is.Not.Empty));
        Assert.That((FinishText(screen), screen.Find("input.distance").GetAttribute("value"), _map.Invocations["showFinish"][^1].Arguments[0]),
            Is.EqualTo(("Pembroke Lodge", "12", (object)PembrokeLodge)));
    }

    //  A slider moved: routes found again at it once it's still, as they can't be re-ranked.
    [Test]
    public void A_slider_moved_routes_are_found_again_at_it()
    {
        _planner.Answer = Planned;
        var screen = RoutesTo(PembrokeLodge);
        screen.Find("input.hills").Input(0.5);
        screen.WaitForState(() => _planner.RoutesAsked.Count == 2, Settling);
        Assert.That(_planner.RoutesAsked[1].Leans, Is.EqualTo(new Leans(Hills: 0.5)));
    }

    //  Undone back to the Loops before: back to the start again, the finish off the map.
    [Test]
    public void Routes_undone_to_Loops_before_end_at_the_start_again()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        EndElsewhere(screen);
        ClickTheMap(screen, PembrokeLodge);
        screen.WaitForState(() => _planner.RoutesAsked.Count == 1, Settling);
        screen.WaitForElement(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That((FinishText(screen), _map.Invocations["clearFinish"].Count > 0),
            Is.EqualTo(("", true))));
    }

    //  T153 (#157): a Point-to-point's Distance over 100 km is 100 km, as a Loop's is.
    [Test]
    public void A_Distance_over_100_km_is_100_km()
    {
        var screen = RoutesTo(PembrokeLodge);
        screen.Find("input.distance").Change(1000);
        Assert.That((screen.Find("input.distance").GetAttribute("value"), screen.Find("input.distance").GetAttribute("oninput")),
            Is.EqualTo(("100", "if (+this.value > 100) this.value = 100")));
    }
}
