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

/// <summary>The planner page (T119, from AskingForALoop): a Run, a Gravel Ride or a Trail Ride, and what each looks for and says.</summary>
public class ChoosingAnActivity : PlannerPageSpecs
{
    private IRenderedComponent<Planner> TrailRideOpened(Activity activity = Activity.TrailRide)
    {
        _planner.Answer = Planned;
        var screen = OpenAfter(new RequestSettings(null, 10, 0, Activity: activity));
        PressGo(screen);
        screen.WaitForElement(".pager .expand").Click();
        return screen;
    }

    //  T068: a Run or a Gravel Ride, chosen in the Request panel and remembered.
    private static void Choose(IRenderedComponent<Planner> screen, string activity) =>
        screen.FindAll(".activity button").Single(b => b.TextContent.Trim() == activity).Click();

    [Test]
    public void A_Request_starts_as_a_Run() =>
        Assert.That(Open(KenwoodHouse).Find(".activity button.chosen").TextContent.Trim(), Is.EqualTo("Run"));

    [Test]
    public void Choosing_Gravel_Ride_finds_Gravel_Ride_Loops_from_the_start()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        Choose(screen, "Gravel Ride");
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That((_planner.Asked[0].Start, _planner.ActivitiesAsked[0]), Is.EqualTo((KenwoodHouse, Activity.GravelRide)));
    }

    [Test]
    public void Go_asks_for_the_Activity_chosen()
    {
        var screen = Open(KenwoodHouse);
        Choose(screen, "Gravel Ride");
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        PressGo(screen);
        screen.WaitForState(() => _planner.Asked.Count == 2, Settling);
        Assert.That(_planner.ActivitiesAsked, Is.All.EqualTo(Activity.GravelRide));
    }

    [Test]
    public void The_Activity_chosen_is_kept_for_next_time()
    {
        var screen = Open(KenwoodHouse);
        Choose(screen, "Gravel Ride");
        screen.WaitForState(() => _last.Saved is not null, Settling);
        Assert.That(_last.Saved?.Activity, Is.EqualTo(Activity.GravelRide));
    }

    [Test]
    public void The_planner_opens_on_the_last_Activity()
    {
        var screen = OpenAfter(new RequestSettings(null, 40, 0, Activity: Activity.GravelRide));
        screen.WaitForState(() => screen.Find(".activity button.chosen").TextContent.Trim() == "Gravel Ride", Settling);
    }

    [Test]
    public void The_car_parks_are_compared_for_the_Activity()
    {
        _carParks.Answer = new([SheenGate], [SheenGate]);
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        SpinWait.SpinUntil(() => _previews.Asked.Count >= 1, Settling);
        Choose(screen, "Gravel Ride");
        // Polled, not asserted till then: NUnit counts every failed Assert, even one retried.
        screen.WaitForState(() => _previews.Activities.ToArray().Contains(Activity.GravelRide), Settling);
    }

    //  T021: a Trail Ride, chosen as a Gravel Ride is; its Breakdown says how much is byway, and it
    //  says to check for closures, linking to findgreenlanes.com (a link only), with the rights of
    //  way's attribution.
    [Test]
    public void Choosing_Trail_Ride_finds_Trail_Ride_Loops_from_the_start()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        Choose(screen, "Trail Ride");
        screen.WaitForState(() => _planner.Asked.Count == 1, Settling);
        Assert.That(_planner.ActivitiesAsked[0], Is.EqualTo(Activity.TrailRide));
    }

    [Test]
    public void A_Trail_Ride_says_how_much_is_byway() =>
        Assert.That(TrailRideOpened().Find(".breakdown tr.byways").TextContent.Trim(), Is.EqualTo("Byways6.02 km"));

    [Test]
    public void A_Trail_Ride_says_to_check_for_closures_before_riding() =>
        Assert.That(TrailRideOpened().Find(".closures").TextContent, Does.Contain("Check for closures before riding"));

    [Test]
    public void A_Trail_Ride_links_to_where_closures_are_reported() =>
        Assert.That(TrailRideOpened().Find(".closures a").GetAttribute("href"), Is.EqualTo("https://findgreenlanes.com/"));

    [Test]
    public void A_Trail_Ride_credits_the_rights_of_way() =>
        Assert.That(TrailRideOpened().Find(".credit").TextContent, Does.Contain("Open Government Licence"));

    [TestCase(Activity.Run)]
    [TestCase(Activity.GravelRide)]
    public void Another_Activity_has_no_byways_or_closures(Activity activity)
    {
        var screen = TrailRideOpened(activity);
        Assert.That(screen.FindAll(".breakdown tr.byways, .closures").Count, Is.Zero);
    }

    //  T073: a Gravel Ride's car parks are those beside a Gravel Off-road network: choosing it
    //  looks around again for it.
    [Test]
    public void Choosing_Gravel_Ride_looks_for_a_Gravel_Ride_s_car_parks()
    {
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        SpinWait.SpinUntil(() => _carParks.Asked.Count >= 1, Settling);
        Choose(screen, "Gravel Ride");
        screen.WaitForAssertion(() => Assert.That(_carParks.Asked[^1].Activity, Is.EqualTo(Activity.GravelRide)), Settling);
    }

    [Test]
    public void A_Run_looks_for_a_Run_s()
    {
        var screen = Open(KenwoodHouse);
        ViewTheMap(screen, Richmond);
        screen.WaitForAssertion(() => Assert.That(_carParks.Asked.Select(a => a.Activity), Is.Not.Empty.And.All.EqualTo(Activity.Run)), Settling);
    }
}
