using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// What the planner calls a start, a finish and a route (T080, T093, T112; moved out of the page in
/// T123): the Start and Finish boxes' words, the name Save offers, and the name a route is exported
/// by. No page: plain values in, words out.
/// </summary>
public class NamingRoutes
{
    private static readonly LatLon KenwoodHouse = new(51.5712, -0.1677);
    private static readonly LatLon ParliamentHill = new(51.5597, -0.1607);
    private static readonly FoundStart SheenGate = new("Sheen Gate", new LatLon(51.4562, -0.2641), 338_000);

    private static PlannedLoop Loop(double lengthM) => new([KenwoodHouse, ParliamentHill, KenwoodHouse], KenwoodHouse, [],
        new Breakdown(LengthM: lengthM, OffRoadPercent: 0, TrafficFreePercent: 0, ConnectorLengthM: 0, RetraceLengthM: 0, FlowM: 0,
            LongestGreenM: 0, MainRoadLengthM: 0, GreeneryPercent: 0));

    //  The Start box: home, a car park chosen, a place's postcode, or a click with none.
    [Test]
    public void No_start_is_nothing() => Assert.That(RouteNames.Start(null, KenwoodHouse, null, null), Is.EqualTo(""));

    [Test]
    public void At_home_it_is_Home() => Assert.That(RouteNames.Start(KenwoodHouse, KenwoodHouse, null, "NW3 7JR"), Is.EqualTo("Home"));

    [Test]
    public void At_a_car_park_chosen_it_is_the_car_parks_name() =>
        Assert.That(RouteNames.Start(SheenGate.At, KenwoodHouse, SheenGate, "SW14 8LZ"), Is.EqualTo("Sheen Gate"));

    [Test]
    public void Away_from_the_car_park_chosen_it_is_its_postcode() =>
        Assert.That(RouteNames.Start(ParliamentHill, KenwoodHouse, SheenGate, "NW5 1QR"), Is.EqualTo("NW5 1QR"));

    [Test]
    public void With_no_home_a_start_is_its_postcode() =>
        Assert.That(RouteNames.Start(ParliamentHill, null, null, "NW5 1QR"), Is.EqualTo("NW5 1QR"));

    [Test]
    public void With_no_postcode_it_was_chosen_on_the_map() =>
        Assert.That(RouteNames.Start(ParliamentHill, KenwoodHouse, null, null), Is.EqualTo("Chosen on the map"));

    //  The Finish box: empty for a Loop.
    [Test]
    public void A_Loop_has_no_finish() => Assert.That(RouteNames.Finish(endElsewhere: false, ParliamentHill, "NW5 1QR"), Is.EqualTo(""));

    [Test]
    public void Ending_elsewhere_with_no_finish_yet_it_is_empty() => Assert.That(RouteNames.Finish(endElsewhere: true, null, "NW5 1QR"), Is.EqualTo(""));

    [Test]
    public void A_finish_is_its_name() => Assert.That(RouteNames.Finish(endElsewhere: true, ParliamentHill, "Parliament Hill"), Is.EqualTo("Parliament Hill"));

    [Test]
    public void A_finish_with_no_name_was_chosen_on_the_map() =>
        Assert.That(RouteNames.Finish(endElsewhere: true, ParliamentHill, null), Is.EqualTo("Chosen on the map"));

    //  The name Save offers (T080): an import's own, else where it starts and its km.
    [Test]
    public void A_Loop_is_offered_its_start_and_km() =>
        Assert.That(RouteNames.Suggested(null, Loop(9_710), toAFinish: false, "Home", ""), Is.EqualTo("Home · 9.7 km"));

    [Test]
    public void A_Point_to_point_is_offered_its_start_its_finish_and_km() =>
        Assert.That(RouteNames.Suggested(null, Loop(8_449), toAFinish: true, "Home", "Parliament Hill"), Is.EqualTo("Home to Parliament Hill · 8.4 km"));

    [Test]
    public void An_import_is_offered_its_own_name() =>
        Assert.That(RouteNames.Suggested("Heath figure of eight", Loop(9_710), toAFinish: true, "Home", "Parliament Hill"), Is.EqualTo("Heath figure of eight"));

    [Test]
    public void With_no_route_shown_nothing_is_offered() => Assert.That(RouteNames.Suggested(null, null, toAFinish: false, "Home", ""), Is.EqualTo(""));

    //  The names Loops were saved or opened by (T112), each Loop by itself.
    [Test]
    public void Saved_by_a_name_it_is_exported_by_it_trimmed()
    {
        var (names, loop) = (new SavedNames(), Loop(9_710));
        names.Saved(loop, "  Sunday hills ");
        Assert.That(names.ExportName(loop, "Home · 9.7 km"), Is.EqualTo("Sunday hills"));
    }

    [Test]
    public void Opened_it_is_exported_by_its_name()
    {
        var (names, loop) = (new SavedNames(), Loop(9_710));
        names.Opened(loop, "Sunday hills");
        Assert.That(names.ExportName(loop, "Home · 9.7 km"), Is.EqualTo("Sunday hills"));
    }

    [Test]
    public void Never_saved_it_is_exported_by_the_name_offered() =>
        Assert.That(new SavedNames().ExportName(Loop(9_710), "Home · 9.7 km"), Is.EqualTo("Home · 9.7 km"));

    [Test]
    public void With_none_shown_the_name_offered() => Assert.That(new SavedNames().ExportName(null, ""), Is.EqualTo(""));

    //  Another Loop the same in every value isn't called by it: each by itself.
    [Test]
    public void Another_Loop_alike_isnt_called_by_its_name()
    {
        var (names, loop) = (new SavedNames(), Loop(9_710));
        var alike = loop with { };
        names.Saved(loop, "Sunday hills");
        Assert.That((alike == loop, names.ExportName(alike, "Home · 9.7 km")), Is.EqualTo((true, "Home · 9.7 km")));
    }

    [Test]
    public void Nudged_it_keeps_its_name()
    {
        var (names, before, after) = (new SavedNames(), Loop(9_710), Loop(10_100));
        names.Saved(before, "Sunday hills");
        names.Carry(before, after);
        Assert.That(names.ExportName(after, "Home · 10.1 km"), Is.EqualTo("Sunday hills"));
    }

    [Test]
    public void Nudged_with_no_name_it_has_none()
    {
        var (names, before, after) = (new SavedNames(), Loop(9_710), Loop(10_100));
        names.Carry(before, after);
        Assert.That(names.ExportName(after, "Home · 10.1 km"), Is.EqualTo("Home · 10.1 km"));
    }
}
