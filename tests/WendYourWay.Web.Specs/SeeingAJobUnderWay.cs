using Bunit;
using WendYourWay.Data.Building;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// A long job shown wherever you are in the planner (#142, T138, STORY-049 AC8; user: "when something
/// long running is happening like a map update so that I know not to close the application"): what
/// it's doing, how long so far, about how long the last of its kind took, and what's safe.
/// </summary>
public class SeeingAJobUnderWay
{
    private static readonly DateTimeOffset Started = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private PlannerScreen _screen = null!;

    [SetUp]
    public void Arrange() => _screen = new PlannerScreen(new FakePlanner());

    [TearDown]
    public void Dispose() => _screen.Dispose();

    private static Job Running(string kind, string stage) => new(5, Started, Started, null, stage, "running", null, "", null, kind);

    // The planner open on its Request panel, this long into the job.
    private string Said(UnderWay? underWay, TimeSpan into)
    {
        (_screen.Admin.UnderWay, _screen.Time.Now) = (underWay, Started + into);
        var page = _screen.Open();
        return page.FindAll(".job-under-way").SingleOrDefault()?.TextContent.Trim() ?? "";
    }

    [Test]
    public void With_nothing_under_way_nothing_is_said() => Assert.That(Said(null, TimeSpan.Zero), Is.Empty);

    [Test]
    public void A_map_update_is_said_on_the_Request_panel_too() =>
        Assert.That(Said(new UnderWay(Running(JobKind.Update, "building"), null), TimeSpan.FromMinutes(72)), Does.StartWith("Updating the map: building"));

    //  Which step of how many (#152, T148): the build's phases each a step, so it moves.
    [Test]
    public void A_map_update_says_which_step_it_s_on_of_how_many() =>
        Assert.That(Said(new UnderWay(Running(JobKind.Update, "building") with { Step = "step 12 of 17, working out the climbing" }, TimeSpan.FromMinutes(200)),
                TimeSpan.FromMinutes(188)),
            Does.StartWith("Updating the map: step 12 of 17, working out the climbing · 3 h 8 min so far, about 3 h 20 min last time."));

    [Test]
    public void It_says_how_long_so_far() =>
        Assert.That(Said(new UnderWay(Running(JobKind.Update, "building"), null), TimeSpan.FromMinutes(72)), Does.Contain("1 h 12 min so far"));

    [Test]
    public void And_about_how_long_the_last_took() =>
        Assert.That(Said(new UnderWay(Running(JobKind.Update, "building"), TimeSpan.FromMinutes(205)), TimeSpan.FromMinutes(72)),
            Does.Contain("1 h 12 min so far, about 3 h 25 min last time"));

    [Test]
    public void Under_a_minute_is_said_so() =>
        Assert.That(Said(new UnderWay(Running(JobKind.Import, "reading 3 files"), TimeSpan.FromSeconds(40)), TimeSpan.FromSeconds(20)),
            Does.Contain("Importing runs: reading 3 files · under a minute so far, about a minute last time"));

    //  What's safe: closing Wend Your Way is (the job runs apart from it); turning the laptop off isn't.
    [Test]
    public void It_says_closing_is_safe_and_turning_off_isnt() =>
        Assert.That(Said(new UnderWay(Running(JobKind.Restore, "restoring"), null), TimeSpan.FromMinutes(3)),
            Does.EndWith("You can close Wend Your Way: it carries on. Keep the laptop on till it's done (a closed lid may still put it to sleep)."));

    [Test]
    public void A_change_of_map_is_said_as_such() =>
        Assert.That(Said(new UnderWay(Running(JobKind.Back, "swapping"), null), TimeSpan.FromMinutes(3)), Does.StartWith("Changing the map: swapping"));
}
