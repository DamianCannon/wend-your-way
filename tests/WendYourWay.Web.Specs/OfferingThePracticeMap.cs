using Bunit;
using WendYourWay.Data;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The practice map offered while there's no map of your own (T162, #166; user chose "Offered when no
/// map": "If no map has been built yet, the app's own screen offers 'Try the practice map'"): through
/// the launcher where there is one; where there isn't (Docker), the command that opens it.
/// </summary>
public class OfferingThePracticeMap
{
    private PlannerScreen _screen = null!;

    [SetUp]
    public void Arrange() => _screen = new PlannerScreen(new FakePlanner());

    [TearDown]
    public void Dispose() => _screen.Dispose();

    private IRenderedComponent<Components.Pages.Planner> Open(bool noMap, bool practice = false)
    {
        _screen.PracticeOffer.NoMap = noMap;
        return _screen.Open(new PlannerSettings(PracticeMap.Start) { Practice = practice });
    }

    [Test]
    public void With_no_map_of_your_own_it_says_so_and_offers_the_practice_map()
    {
        var screen = Open(noMap: true);
        screen.WaitForAssertion(() => Assert.That(
            (screen.Find(".panel .no-map p").TextContent.StartsWith("There's no map of your own yet", StringComparison.Ordinal), screen.Find(".panel .no-map button").TextContent),
            Is.EqualTo((true, "Try the practice map"))));
    }

    [Test]
    public void Tried_it_opens_in_its_own_window()
    {
        var screen = Open(noMap: true);
        screen.WaitForAssertion(() => screen.Find(".panel .no-map button").Click());
        screen.WaitForAssertion(() => Assert.That((_screen.PracticeOffer.Opened, screen.Find(".panel .no-map .opening").TextContent),
            Is.EqualTo((1, "Opening the practice map in a window of its own…"))));
    }

    //  No launcher to open it with (Docker): the command that does.
    [Test]
    public void With_no_launcher_it_says_how_to_open_it()
    {
        _screen.PracticeOffer.CanOpen = false;
        _screen.PracticeOffer.HowToOpen = "docker compose --profile practice up -d practice, then http://127.0.0.1:5020";
        var screen = Open(noMap: true);
        screen.WaitForAssertion(() => Assert.That((screen.Find(".panel .no-map .how").TextContent, screen.FindAll(".panel .no-map button").Count),
            Is.EqualTo(("To try it: docker compose --profile practice up -d practice, then http://127.0.0.1:5020.", 0))));
    }

    [Test]
    public void With_a_map_of_your_own_nothing_is_offered() =>
        Assert.That(Open(noMap: false).FindAll(".no-map"), Is.Empty);

    [Test]
    public void On_the_practice_map_itself_nothing_is_offered() =>
        Assert.That(Open(noMap: true, practice: true).FindAll(".no-map"), Is.Empty);

    //  With a map of your own, Help says how to open it.
    [Test]
    public void Help_says_how_to_open_it()
    {
        var screen = Open(noMap: false);
        screen.Find(".rail button[aria-label='Help']").Click();
        Assert.That(screen.Find(".panel .practice-help").TextContent,
            Is.EqualTo("To try Wend Your Way on a practice map of Richmond Park, without touching your own: the Wend Your Way – practice map shortcut in the Start menu."));
    }
}

/// <summary>How the practice map is opened, where (T162).</summary>
public class SayingHowToOpenThePracticeMap
{
    [Test]
    public void With_the_launcher_its_shortcut() =>
        Assert.That(PracticeOffer.HowTo(launcher: true, inContainer: false), Is.EqualTo("the Wend Your Way – practice map shortcut in the Start menu"));

    [Test]
    public void In_Docker_its_service() =>
        Assert.That(PracticeOffer.HowTo(launcher: false, inContainer: true), Is.EqualTo("docker compose --profile practice up -d practice, then http://127.0.0.1:5020"));

    [Test]
    public void Otherwise_wend_and_the_planner_by_hand() =>
        Assert.That(PracticeOffer.HowTo(launcher: false, inContainer: false),
            Is.EqualTo("dotnet run -c Release --project src/WendYourWay.Cli -- practice, then the planner with --Practice=true"));
}
