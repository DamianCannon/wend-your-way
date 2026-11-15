using WendYourWay.Launching;

namespace WendYourWay.Core.Specs.Launching;

/// <summary>
/// Opened with one click (#142, T138, STORY-049; user: "I want a one click method to start it up and
/// open the page"): what the launcher decides before it shows anything, without starting anything.
/// </summary>
public class OpeningWithOneClick
{
    private static readonly DateTime Built = new(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);

    private static Source At(string path, int minutesAfterTheBuild) => new(path, Built.AddMinutes(minutesAfterTheBuild));

    //  AC4 (user chose "Rebuild first"): the app rebuilt if its code changed since it was built.
    [Test]
    public void Code_changed_since_the_build_needs_a_rebuild() =>
        Assert.That(Rebuild.Needed([At(@"src\WendYourWay.Web\Components\AdminPanel.razor", 5)], Built), Is.True);

    [Test]
    public void Code_unchanged_since_needs_none() =>
        Assert.That(Rebuild.Needed([At(@"src\WendYourWay.Web\Components\AdminPanel.razor", -5), At(@"db\functions\cover_outings.sql", -60)], Built), Is.False);

    [Test]
    public void Never_built_it_needs_one() => Assert.That(Rebuild.Needed([At(@"src\WendYourWay.Core\Loop.cs", -5)], null), Is.True);

    //  What a build writes, and the launcher itself (it can't rebuild itself while it runs), aren't code to rebuild for.
    [TestCase(@"src\WendYourWay.Web\bin\Release\net10.0\WendYourWay.Web.dll")]
    [TestCase(@"src\WendYourWay.Data\obj\Release\net10.0\WendYourWay.Data.AssemblyInfo.cs")]
    [TestCase(@"src\WendYourWay.Launcher\MainWindow.cs")]
    [TestCase(@"src\WendYourWay.Launching\Rebuild.cs")]
    public void Builds_and_the_launcher_dont_count(string path) =>
        Assert.That(Rebuild.Needed([At(path, 5)], Built), Is.False);

    [TestCase(@"src\WendYourWay.Data\Building\Jobs.cs")]
    [TestCase(@"db\migrations\0047_more.sql")]
    [TestCase(@"Directory.Build.props")]
    public void Code_the_app_is_built_from_counts(string path) =>
        Assert.That(Rebuild.Needed([At(path, 5)], Built), Is.True);

    //  AC5 (user chose "Bring it to the front"), AC6: what to do, from what's found.
    [Test]
    public void Ours_already_open_is_brought_to_the_front() =>
        Assert.That(Launch.Decide(ourWindowOpen: true, App.None, postgreSqlUp: true), Is.EqualTo(Launch.BringToFront));

    [Test]
    public void Nothing_running_it_starts_the_app() =>
        Assert.That(Launch.Decide(ourWindowOpen: false, App.None, postgreSqlUp: true), Is.EqualTo(Launch.StartTheApp));

    //  Started some other way (by hand): a window onto it, which leaves it running when it closes.
    [Test]
    public void The_app_already_running_it_opens_a_window_onto_it() =>
        Assert.That(Launch.Decide(ourWindowOpen: false, App.Ours, postgreSqlUp: true), Is.EqualTo(Launch.OpenOntoIt));

    [Test]
    public void Something_else_on_its_port_is_said() =>
        Assert.That(Launch.Decide(ourWindowOpen: false, App.SomethingElse, postgreSqlUp: true), Is.EqualTo(Launch.PortTaken));

    [Test]
    public void PostgreSQL_not_running_is_said() =>
        Assert.That(Launch.Decide(ourWindowOpen: false, App.None, postgreSqlUp: false), Is.EqualTo(Launch.PostgreSqlDown));

    [Test]
    public void A_page_is_WendYourWays_by_its_title() =>
        Assert.That((App.Of("<html><head><title>Wend Your Way</title>"), App.Of("<title>Something else</title>"), App.Of(null)),
            Is.EqualTo((App.Ours, App.SomethingElse, App.None)));

    //  The address it opens: Wend Your Way's own, or the one given (--urls), for trying it on another port.
    [Test]
    public void It_opens_the_apps_own_address() =>
        Assert.That(Launch.Address([]), Is.EqualTo(new Uri("http://127.0.0.1:5018/")));

    [Test]
    public void Or_the_one_given() =>
        Assert.That(Launch.Address(["--Home:Latitude=51.4436", "--urls", "http://127.0.0.1:5019"]), Is.EqualTo(new Uri("http://127.0.0.1:5019/")));

    //  AC6: said plainly.
    [Test]
    public void Each_reason_it_cant_start_is_said() =>
        Assert.That(new[] { Launch.PostgreSqlDown, Launch.PortTaken }.Select(Launch.Said),
            Is.EqualTo(new[]
            {
                "PostgreSQL isn't running, so Wend Your Way can't start. Start it (the postgresql-17 service), then try again.",
                "Something other than Wend Your Way is using its address (127.0.0.1:5018). Close it, then try again.",
            }));
}
