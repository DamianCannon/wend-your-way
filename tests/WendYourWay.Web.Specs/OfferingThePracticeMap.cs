using Bunit;
using WendYourWay.Data;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The practice map offered while there's no map of your own (T162, #166; user chose "Offered when no
/// map"), and switched to and back from Help (T163, #167; user: "I'd rather just have a button in the
/// help panel that switches to the practice map (and back again)"): in the launcher's window, the
/// window asked to switch; in a browser, how to open it.
/// </summary>
public class OfferingThePracticeMap
{
    private PlannerScreen _screen = null!;

    [SetUp]
    public void Arrange() => _screen = new PlannerScreen(new FakePlanner());

    [TearDown]
    public void Dispose() => _screen.Dispose();

    private IRenderedComponent<Components.Pages.Planner> Open(bool noMap = false, bool practice = false, bool inWindow = true)
    {
        _screen.PracticeOffer.NoMap = noMap;
        _screen.Host.Setup<bool>("inWindow").SetResult(inWindow);
        return _screen.Open(new PlannerSettings(PracticeMap.Start) { Practice = practice });
    }

    private static IRenderedComponent<Components.Pages.Planner> InHelp(IRenderedComponent<Components.Pages.Planner> screen)
    {
        screen.Find(".rail button[aria-label='Help']").Click();
        return screen;
    }

    private IEnumerable<object?> AskedToSwitchTo => _screen.Host.Invocations["switchMap"].Select(i => i.Arguments[0]);

    [Test]
    public void With_no_map_of_your_own_it_says_so_and_offers_the_practice_map()
    {
        var screen = Open(noMap: true);
        screen.WaitForAssertion(() => Assert.That(
            (screen.Find(".panel .no-map p").TextContent.StartsWith("There's no map of your own yet", StringComparison.Ordinal), screen.Find(".panel .no-map button").TextContent),
            Is.EqualTo((true, "Try the practice map"))));
    }

    [Test]
    public void Tried_the_window_is_asked_to_switch_to_it()
    {
        var screen = Open(noMap: true);
        screen.WaitForAssertion(() => screen.Find(".panel .no-map button").Click());
        screen.WaitForAssertion(() => Assert.That((AskedToSwitchTo, screen.Find(".panel .no-map .switching").TextContent),
            Is.EqualTo((new object?[] { "practice" }, "Getting the practice map ready…"))));
    }

    //  In a browser, no window to switch: the command that opens it.
    [Test]
    public void In_a_browser_it_says_how_to_open_it()
    {
        var screen = Open(noMap: true, inWindow: false);
        screen.WaitForAssertion(() => Assert.That((screen.Find(".panel .no-map .how").TextContent, screen.FindAll(".panel .no-map button").Count),
            Is.EqualTo(("To try it: docker compose --profile practice up -d practice, then http://127.0.0.1:5020.", 0))));
    }

    [Test]
    public void With_a_map_of_your_own_nothing_is_offered() =>
        Assert.That(Open(noMap: false).FindAll(".no-map"), Is.Empty);

    [Test]
    public void On_the_practice_map_itself_nothing_is_offered() =>
        Assert.That(Open(noMap: true, practice: true).FindAll(".no-map"), Is.Empty);

    [Test]
    public void Help_switches_to_the_practice_map()
    {
        var screen = InHelp(Open());
        screen.WaitForAssertion(() => screen.Find(".panel .practice-help .switch-map").Click());
        screen.WaitForAssertion(() => Assert.That(AskedToSwitchTo, Is.EqualTo(new object?[] { "practice" })));
    }

    [Test]
    public void Help_says_what_it_switches_to() =>
        Assert.That(InHelp(Open()).WaitForElement(".panel .practice-help .switch-map").TextContent, Is.EqualTo("Switch to the practice map"));

    [Test]
    public void On_the_practice_map_Help_goes_back_to_your_own()
    {
        var screen = InHelp(Open(practice: true));
        var back = screen.WaitForElement(".panel .practice-help .switch-map");
        Assert.That(back.TextContent, Is.EqualTo("Back to my map"));
        back.Click();
        screen.WaitForAssertion(() => Assert.That((AskedToSwitchTo, screen.Find(".panel .practice-help .switching").TextContent),
            Is.EqualTo((new object?[] { "own" }, "Going back to your own map…"))));
    }

    [Test]
    public void In_a_browser_Help_says_how_to_open_it()
    {
        var screen = InHelp(Open(inWindow: false));
        screen.WaitForAssertion(() => Assert.That((screen.Find(".panel .practice-help .how").TextContent, screen.FindAll(".panel .practice-help button").Count),
            Is.EqualTo(("To open it: docker compose --profile practice up -d practice, then http://127.0.0.1:5020.", 0))));
    }
}

/// <summary>How the practice map is opened in a browser (T162, T163).</summary>
public class SayingHowToOpenThePracticeMap
{
    [Test]
    public void In_Docker_its_service() =>
        Assert.That(PracticeOffer.HowTo(inContainer: true), Is.EqualTo("docker compose --profile practice up -d practice, then http://127.0.0.1:5020"));

    [Test]
    public void Otherwise_the_launcher_or_wend_and_the_planner_by_hand() =>
        Assert.That(PracticeOffer.HowTo(inContainer: false),
            Is.EqualTo("open Wend Your Way from its icon and switch to it here, or run wend practice and the planner with --Practice=true"));
}
