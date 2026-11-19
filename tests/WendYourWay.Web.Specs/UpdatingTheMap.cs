using Bunit;
using WendYourWay.Data.Building;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The map's update in the planner (PLAN T128, STORY-047; user: "a single click way to update the
/// application so that it has the latest OSM maps"): under the date of the map in use, whether
/// Geofabrik has a newer one, a button to update to it, how a running update is getting on, and how
/// the last one ended.
/// </summary>
public class UpdatingTheMap
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 21, 22, 17, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);
    private static readonly DateTimeOffset Started = new(2026, 11, 12, 10, 47, 0, TimeSpan.Zero);

    private PlannerScreen _screen = null!;

    [SetUp]
    public void Arrange() => _screen = new PlannerScreen(new FakePlanner());

    [TearDown]
    public void Dispose() => _screen.Dispose();

    private IRenderedComponent<Planner> Open(MapUpdateState state)
    {
        _screen.MapUpdater.State = state;
        var page = _screen.Open();
        page.WaitForElement(".map-update");
        return page;
    }

    private static MapUpdateState State(MapUpdateKind kind, string? stage = null, string? message = null, DateTimeOffset? sourceAt = null) =>
        new(kind, stage, kind == MapUpdateKind.None ? null : Started, message, sourceAt, Monday, MapBuiltAt: Started);

    private static string Said(IRenderedComponent<Planner> page) => page.Find(".map-update").TextContent;

    //  T150 (#154; user: "just say something like latest map available as of XXX or if not available
    //  then say latest map not available"): the map in use, by its paths' date and when it was built.
    [Test]
    public void It_says_which_map_is_in_use() =>
        Assert.That(Said(Open(State(MapUpdateKind.None, sourceAt: Monday))), Does.Contain("In use: paths as of 9 Nov 2026, built 12 Nov 2026."));

    [Test]
    public void A_newer_map_is_offered_with_its_date()
    {
        var page = Open(State(MapUpdateKind.None, sourceAt: Tuesday));
        Assert.That((Said(page).Contains("Latest map available: 10 Nov 2026.", StringComparison.Ordinal), page.FindAll(".map-update button.update").Count), Is.EqualTo((true, 1)));
    }

    [Test]
    public void Up_to_date_it_says_so_and_offers_no_update()
    {
        var page = Open(State(MapUpdateKind.None, sourceAt: Monday));
        Assert.That((Said(page).Contains("Latest map available: 9 Nov 2026.", StringComparison.Ordinal), page.FindAll(".map-update button.update").Count), Is.EqualTo((true, 0)));
    }

    [Test]
    public void Geofabrik_not_answering_it_says_the_latest_isnt_available()
    {
        var page = Open(State(MapUpdateKind.None));
        Assert.That((Said(page).Contains("Latest map not available.", StringComparison.Ordinal), page.FindAll(".map-update button.update").Count), Is.EqualTo((true, 0)));
    }

    [Test]
    public void The_button_starts_an_update()
    {
        var page = Open(State(MapUpdateKind.None, sourceAt: Tuesday));
        page.Find(".map-update button.update").Click();
        page.WaitForAssertion(() => Assert.That(_screen.MapUpdater.Started, Is.EqualTo(1)));
    }

    [Test]
    public void Started_it_says_it_has_begun()
    {
        var page = Open(State(MapUpdateKind.None, sourceAt: Tuesday));
        _screen.MapUpdater.StateAfterStart = State(MapUpdateKind.Running, "checking", sourceAt: Tuesday);
        page.Find(".map-update button.update").Click();
        page.WaitForAssertion(() => Assert.That(Said(page), Does.Contain("Updating the map: checking")));
    }

    [Test]
    public void Not_started_it_says_why()
    {
        var page = Open(State(MapUpdateKind.None, sourceAt: Tuesday));
        _screen.MapUpdater.Refusal = "wend isn't built: run dotnet build -c Release src/WendYourWay.Cli once.";
        page.Find(".map-update button.update").Click();
        page.WaitForAssertion(() => Assert.That(Said(page), Does.Contain("wend isn't built")));
    }

    [Test]
    public void Running_it_says_its_stage_and_offers_no_second()
    {
        var page = Open(State(MapUpdateKind.Running, "building", sourceAt: Tuesday));
        Assert.That((Said(page).Contains("Updating the map: building", StringComparison.Ordinal), page.FindAll(".map-update button.update").Count), Is.EqualTo((true, 0)));
    }

    [Test]
    public void Stopped_part_way_it_says_where_and_offers_to_try_again()
    {
        var page = Open(State(MapUpdateKind.Stopped, "downloading", sourceAt: Tuesday));
        Assert.That((Said(page).Contains("stopped part-way, downloading", StringComparison.Ordinal), page.Find(".map-update button.update").TextContent.Trim()),
            Is.EqualTo((true, "Try again")));
    }

    [Test]
    public void Failed_it_says_why_and_offers_to_try_again()
    {
        var page = Open(State(MapUpdateKind.Failed, "downloading", "The download didn't match its checksum.", Tuesday));
        Assert.That((Said(page).Contains("The download didn't match its checksum.", StringComparison.Ordinal), page.Find(".map-update button.update").TextContent.Trim()),
            Is.EqualTo((true, "Try again")));
    }

    //  T132: while another job runs (an import, a restore), no update can start, nor try again.
    [Test]
    public void Another_job_running_no_update_can_start() =>
        Assert.That(Open(State(MapUpdateKind.None, sourceAt: Tuesday) with { Busy = true }).Find(".map-update button.update").HasAttribute("disabled"), Is.True);

    [Test]
    public void Another_job_running_no_update_can_be_tried_again() =>
        Assert.That(Open(State(MapUpdateKind.Stopped, "downloading", sourceAt: Tuesday) with { Busy = true }).Find(".map-update button.update").HasAttribute("disabled"), Is.True);

    [Test]
    public void With_nothing_else_running_it_can() =>
        Assert.That(Open(State(MapUpdateKind.None, sourceAt: Tuesday)).Find(".map-update button.update").HasAttribute("disabled"), Is.False);

    [Test]
    public void Finished_it_says_the_map_now_in_use_not_the_last_updates_own_words()
    {
        var page = Open(State(MapUpdateKind.Finished, "backing up", "Updated: the map's paths are as of 10 Nov 2026.", Tuesday) with { MapAt = Tuesday });
        Assert.That((Said(page).Contains("In use: paths as of 10 Nov 2026, built 12 Nov 2026.", StringComparison.Ordinal), Said(page).Contains("Updated:", StringComparison.Ordinal)),
            Is.EqualTo((true, false)));
    }
}
