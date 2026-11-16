using Bunit;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// Opened while a map update, a build or another job runs (#153, T149): the app's own database changes
/// held back till then, and the planner says so, and what to do once it's done.
/// </summary>
public class OpeningWhileTheMapIsBuilt
{
    private PlannerScreen _screen = null!;

    [SetUp]
    public void Arrange() => _screen = new PlannerScreen(new FakePlanner());

    [TearDown]
    public void Dispose() => _screen.Dispose();

    private string Said(HeldBack? heldBack)
    {
        _screen.Admin.HeldBack = heldBack;
        var page = _screen.Open();
        return page.FindAll(".held-back").SingleOrDefault()?.TextContent.Trim() ?? "";
    }

    [Test]
    public void With_nothing_held_back_nothing_is_said() => Assert.That(Said(null), Is.Empty);

    [Test]
    public void While_it_runs_it_says_the_changes_wait_for_it() =>
        Assert.That(Said(new HeldBack(StillRunning: true)),
            Is.EqualTo("A map update or build is running, so Wend Your Way opened without its latest changes to the database. "
                + "Some things may not work till it's done and you've closed Wend Your Way and opened it again."));

    [Test]
    public void Once_it_s_done_it_says_to_open_it_again() =>
        Assert.That(Said(new HeldBack(StillRunning: false)),
            Is.EqualTo("The map update or build is done: close Wend Your Way and open it again to finish bringing it up to date."));
}
