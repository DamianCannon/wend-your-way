using Bunit;
using WendYourWay.Web.Components;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace WendYourWay.Web.Specs;

/// <summary>The planner screen's layout (SPEC §10): a full-screen map, a left panel with its icon rail, and a pager card.</summary>
public class UsingThePlannerScreen
{
    private PlannerScreen _screen = null!;
    private BunitContext _context = null!;

    [SetUp]
    public void Arrange()
    {
        _screen = new PlannerScreen(new NeverFindsALoop());
        _context = _screen.Context;
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public void Dispose() => _screen.Dispose();

    // Opened, and its map told what to show as the page's first render finishes (T188: the Activity, its last call
    // there), so a click isn't queued behind that and lost; as PlannerPageSpecs waits.
    private IRenderedComponent<Planner> Open()
    {
        var screen = _context.Render<Planner>();
        screen.WaitForAssertion(() => Assert.That(_screen.Map.Invocations["showActivity"], Is.Not.Empty));
        return screen;
    }

    //  No Reference Loops (T109): the user's stars took their place. Admin (T129) before Help.
    [Test]
    public void The_rail_offers_every_panel() =>   // R10.1
        Assert.That(
            Open().FindAll(".rail button").Select(b => b.GetAttribute("aria-label")),
            Is.EqualTo(new[] { "Request", "Saved Routes", "Outings", "Admin", "Help" }));

    [Test]
    public void The_Request_panel_is_open_to_begin_with() =>
        Assert.That(Open().Find(".panel h2").TextContent, Is.EqualTo("Request"));

    [Test]
    public void Choosing_from_the_rail_opens_that_panel()
    {
        var screen = Open();
        screen.Find(".rail button[aria-label='Outings']").Click();
        Assert.That(screen.Find(".panel h2").TextContent, Is.EqualTo("Outings"));
    }

    [Test]
    public void Collapsing_the_panel_leaves_only_the_rail()   // R10.1
    {
        var screen = Open();
        screen.Find("button.collapse").Click();
        Assert.Multiple(() =>
        {
            Assert.That(screen.FindAll(".panel"), Is.Empty);
            Assert.That(screen.FindAll(".rail button[aria-label]"), Has.Count.EqualTo(5));
        });
    }

    [Test]
    public void Choosing_from_the_rail_opens_a_collapsed_panel_again()
    {
        var screen = Open();
        screen.Find("button.collapse").Click();
        screen.Find(".rail button[aria-label='Help']").Click();
        Assert.That(screen.Find(".panel h2").TextContent, Is.EqualTo("Help"));
    }

    [Test]
    public void The_Help_panel_links_to_how_WendYourWay_works()
    {
        var screen = Open();
        screen.Find(".rail button[aria-label='Help']").Click();
        Assert.That(screen.Find(".panel a").GetAttribute("href"), Is.EqualTo("how-it-works"));
    }

    [Test]
    public void Before_any_Request_the_pager_card_says_there_are_no_Loops_yet() =>   // R10.5
        Assert.That(Open().Find(".pager").TextContent.Trim(), Is.EqualTo("No Loops yet"));

    private sealed class NeverFindsALoop : ILoopPlanner
    {
        public Task<IOfferedLoops> PlanPointToPointAsync(
            WendYourWay.Data.LatLon start, WendYourWay.Data.LatLon finish, double? distanceKm = null, WendYourWay.Core.Loops.Leans? leans = null, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IOfferedLoops> ReopenAsync(
            PlannedLoop saved, IReadOnlyList<(WendYourWay.Core.Network.SegmentKey Key, bool Forward)> traversals, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IOfferedLoops> ImportAsync(IReadOnlyList<WendYourWay.Data.LatLon> route, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IOfferedLoops> PlanAsync(WendYourWay.Data.LatLon start, double distanceKm, WendYourWay.Core.Loops.Leans? leans = null, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, Action<PlanningProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IOfferedLoops>(new FakeOffer([]));
    }
}
