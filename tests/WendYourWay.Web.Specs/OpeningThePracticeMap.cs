using Bunit;
using Microsoft.Extensions.Configuration;
using WendYourWay.Data;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The planner on the practice map (T161, #165; user: "ok so now the demo map?"): Richmond Park and
/// around, starting in the park whatever Home is set; saying it's the practice map and nothing done
/// there is kept; and nothing that changes the map or backs up, Admin saying how to build your own.
/// </summary>
public class OpeningThePracticeMap
{
    private static PlannerSettings From(params (string Key, string Value)[] settings) =>
        PlannerSettings.From(new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build());

    private static readonly (string, string)[] AHome = [("Home:Latitude", "51.5712"), ("Home:Longitude", "-0.1677")];

    [Test]
    public void On_it_Requests_start_in_the_park_whatever_Home_is_set() =>
        Assert.That(From([.. AHome, ("Practice", "true")]), Is.EqualTo(new PlannerSettings(PracticeMap.Start) { Practice = true }));

    [Test]
    public void Otherwise_they_start_at_Home() =>
        Assert.That(From(AHome), Is.EqualTo(new PlannerSettings(new LatLon(51.5712, -0.1677))));

    private PlannerScreen _screen = null!;

    [SetUp]
    public void Arrange() => _screen = new PlannerScreen(new FakePlanner());

    [TearDown]
    public void Dispose() => _screen.Dispose();

    private IRenderedComponent<Components.Pages.Planner> Open(bool practice = true) =>
        _screen.Open(new PlannerSettings(PracticeMap.Start) { Practice = practice });

    [Test]
    public void The_Request_panel_says_it_s_the_practice_map_and_nothing_is_kept()
    {
        var screen = Open();
        Assert.That((screen.Find(".panel .practice").TextContent, screen.FindAll(".panel .map-update").Count),
            Is.EqualTo(("The practice map: Richmond Park and around. Nothing done here is kept: it's emptied each time it opens.", 0)));
    }

    [Test]
    public void Admin_says_how_to_build_your_own_and_offers_nothing_that_changes_the_map()
    {
        var screen = Open();
        screen.Find(".rail button[aria-label='Admin']").Click();
        Assert.That((screen.Find(".panel .practice").TextContent.Contains("build your own region", StringComparison.Ordinal), screen.FindAll(".panel .admin").Count),
            Is.EqualTo((true, 0)));
    }

    [Test]
    public void On_your_own_map_it_says_nothing_of_practice() =>
        Assert.That(Open(practice: false).FindAll(".practice"), Is.Empty);
}

/// <summary>Its start called by its name, not Home (T161): it's nobody's home.</summary>
public class NamingThePracticeMapsStart
{
    [Test]
    public void Its_start_is_called_Pembroke_Lodge() =>
        Assert.That(new PlannerSettings(PracticeMap.Start) { Practice = true }.HomeName, Is.EqualTo("Pembroke Lodge"));

    [Test]
    public void Home_is_called_Home() =>
        Assert.That(new PlannerSettings(PracticeMap.Start).HomeName, Is.EqualTo("Home"));

    [Test]
    public void The_Start_box_says_it() =>
        Assert.That(RouteNames.Start(PracticeMap.Start, PracticeMap.Start, null, null, "Pembroke Lodge"), Is.EqualTo("Pembroke Lodge"));
}
