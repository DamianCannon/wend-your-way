using Bunit;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Components;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Web.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace WendYourWay.Web.Specs;

/// <summary>
/// STORY-030 on the planner screen (PLAN T017): the Outings panel lists the user's imported runs,
/// and one at a time is drawn over the Segments it covered, with its gaps listed. The Outings are
/// stand-ins on Hampstead Heath, never the user's.
/// </summary>
public class ReviewingOutings
{
    private static readonly LatLon ParliamentHill = new(51.5597, -0.1607);
    private static readonly LatLon KenwoodHouse = new(51.5712, -0.1677);

    private static readonly OutingSummary HeathRun = new(7, "Heath Run", new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero), 9_420, 0.93, 2);

    private static readonly OutingView HeathRunView = new(7, "Heath Run",
        Track: [ParliamentHill, KenwoodHouse],
        Covered: [new CoveredSegment([ParliamentHill, KenwoodHouse], GroundKind.OffRoad), new CoveredSegment([KenwoodHouse, ParliamentHill], GroundKind.Connector)],
        Gaps: [new OutingGap([ParliamentHill, KenwoodHouse], 142.4), new OutingGap([KenwoodHouse, ParliamentHill], 61)])
    {
        Activity = WendYourWay.Core.Loops.Activity.GravelRide,
        // Its whole recording, every point: what a route is made from (user, 2026-11-09).
        RouteTrack = [ParliamentHill, new LatLon(51.5650, -0.1640), KenwoodHouse],
    };

    private static readonly OutingSummary LakeRun = new(8, "Lake Run", new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero), 5_100, 0.98, 0);

    private static readonly OutingView LakeRunView = new(8, "Lake Run",
        Track: [KenwoodHouse, ParliamentHill], Covered: [new CoveredSegment([KenwoodHouse, ParliamentHill], GroundKind.OffRoad)], Gaps: []);

    private static readonly OutingSummary CanalRun = new(9, "Canal Run", new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero), 7_300, 0.9, 0);

    private static readonly OutingView CanalRunView = new(9, "Canal Run",
        Track: [ParliamentHill, KenwoodHouse], Covered: [new CoveredSegment([ParliamentHill, KenwoodHouse], GroundKind.TrafficFree)], Gaps: []);

    private PlannerScreen _screen = null!;
    private BunitContext _context = null!;
    private BunitJSModuleInterop _map = null!;
    private FakeOutings _outings = null!;
    private OneLoopPlanner _planner = null!;

    [SetUp]
    public void Arrange()
    {
        _screen = new PlannerScreen(_planner = new OneLoopPlanner(), _outings = new FakeOutings());
        (_context, _map) = (_screen.Context, _screen.Map);
    }

    [TearDown]
    public void Dispose() => _screen.Dispose();

    private IRenderedComponent<Planner> OpenOutings()
    {
        var screen = _context.Render<Planner>();
        screen.Find(".rail button[aria-label='Outings']").Click();
        return screen;
    }

    [Test]
    public void The_Outings_panel_lists_each_Outing_with_what_it_covered()   // AC1
    {
        _outings.List = [HeathRun];
        var screen = OpenOutings();
        screen.WaitForAssertion(() => Assert.That(
            screen.FindAll(".outings li > button").Select(li => li.TextContent.Trim().ReplaceLineEndings(" ")),
            Is.EqualTo(new[] { "Heath Run 18 Oct 2026 · 9.4 km · 93% covered · 2 gaps" })));
    }

    //  The app's stars, then the user's right under them, lined up (user, 2026-11-10).
    [Test]
    public void The_app_s_stars_and_the_user_s_sit_one_under_the_other()
    {
        _outings.List = [HeathRun with { Stars = 3, Scored = true }];
        var screen = OpenOutings();
        var stars = screen.WaitForElement(".outings > li > .stars");
        Assert.That((stars.Children.Select(c => c.ClassName).ToArray(), stars.QuerySelectorAll(".app-stars .star").Length, stars.QuerySelectorAll(".my-stars button").Length),
            Is.EqualTo((new[] { "app-stars", "my-stars" }, 5, 5)));
    }

    //  T097 (STORY-045 AC2): the app's stars for each Outing, from its score.
    private string AppStars(int? stars, bool scored = true)
    {
        _outings.List = [HeathRun with { Stars = stars, Scored = scored }];
        var screen = OpenOutings();
        screen.WaitForAssertion(() => Assert.That(screen.FindAll(".outings li"), Is.Not.Empty));
        var shown = screen.Find(".outings li .app-stars");
        return $"{shown.TextContent.Trim()} | {shown.GetAttribute("aria-label")}";
    }

    [Test]
    public void Each_Outing_shows_the_app_s_stars_for_it() =>
        Assert.That(AppStars(3), Is.EqualTo("App ★★★☆☆ | The app's stars: 3 of 5"));

    //  T097 (STORY-045 AC1): the user gives an Outing their own stars, beside the app's.
    private IRenderedComponent<Planner> ListedWith(int? userStars, int? appStars = null)
    {
        _outings.List = [HeathRun with { UserStars = userStars, Stars = appStars, Scored = true }];
        var screen = OpenOutings();
        screen.WaitForAssertion(() => Assert.That(screen.FindAll(".outings li .my-stars button"), Has.Count.EqualTo(5)));
        return screen;
    }

    private static string[] Pressed(IRenderedComponent<Planner> screen) =>
        [.. screen.FindAll(".outings li .my-stars button").Select(b => b.GetAttribute("aria-pressed") ?? "")];

    [Test]
    public void Clicking_a_star_gives_an_Outing_that_many()
    {
        var screen = ListedWith(null);
        screen.FindAll(".outings li .my-stars button")[3].Click();
        screen.WaitForAssertion(() => Assert.That(_outings.Rated, Is.EqualTo(new[] { (7, (int?)4) })));
        Assert.That(Pressed(screen), Is.EqualTo(new[] { "true", "true", "true", "true", "false" }));
    }

    //  Whose stars are whose (user, 2026-11-09: "labels to indicate your auto-rating and my human-rating").
    [Test]
    public void The_user_s_stars_are_labelled_as_theirs() =>
        Assert.That(ListedWith(3).Find(".outings li .my-stars .who").TextContent.Trim(), Is.EqualTo("You"));

    [Test]
    public void The_user_s_stars_are_shown_as_given() =>
        Assert.That(Pressed(ListedWith(2)), Is.EqualTo(new[] { "true", "true", "false", "false", "false" }));

    [Test]
    public void Clicking_the_star_given_takes_the_stars_away()
    {
        var screen = ListedWith(3);
        screen.FindAll(".outings li .my-stars button")[2].Click();
        screen.WaitForAssertion(() => Assert.That(_outings.Rated, Is.EqualTo(new[] { (7, (int?)null) })));
        Assert.That(Pressed(screen), Is.All.EqualTo("false"));
    }

    [Test]
    public void Each_star_says_what_clicking_it_does()
    {
        var screen = ListedWith(3);
        Assert.That(screen.FindAll(".outings li .my-stars button").Select(b => b.GetAttribute("aria-label")),
            Is.EqualTo(new[] { "Give it 1 star", "Give it 2 stars", "Take away your 3 stars", "Give it 4 stars", "Give it 5 stars" }));
    }

    [Test]
    public void Rating_an_Outing_doesnt_draw_it()
    {
        var screen = ListedWith(null);
        screen.FindAll(".outings li .my-stars button")[0].Click();
        screen.WaitForAssertion(() => Assert.That(_outings.Rated, Is.Not.Empty));
        Assert.That(_map.Invocations.Count(i => i.Identifier == "showOuting"), Is.Zero);
    }

    //  T097 (STORY-045 AC3): where the user and the app are 2 stars or more apart, it stands out.
    [TestCase(4, 1, true)]
    [TestCase(1, 3, true)]
    [TestCase(4, 3, false)]
    [TestCase(3, 3, false)]
    [TestCase(null, 1, false)]
    [TestCase(4, null, false)]
    public void Where_the_user_and_the_app_are_2_stars_apart_it_stands_out(int? user, int? app, bool standsOut)
    {
        _outings.List = [HeathRun with { UserStars = user, Stars = app }];
        var screen = OpenOutings();
        screen.WaitForAssertion(() => Assert.That(screen.FindAll(".outings li"), Is.Not.Empty));
        Assert.That(screen.Find(".outings li").ClassList.Contains("disagree"), Is.EqualTo(standsOut));
    }

    [Test]
    public void It_says_how_far_apart_they_are()
    {
        _outings.List = [HeathRun with { UserStars = 5, Stars = 2 }];
        var screen = OpenOutings();
        Assert.That(screen.WaitForElement(".outings li .apart").TextContent.Trim(), Is.EqualTo("You gave it 5 stars, the app 2"));
    }

    [Test]
    public void Rating_it_against_the_app_makes_it_stand_out()
    {
        var screen = ListedWith(null, appStars: 1);
        screen.FindAll(".outings li .my-stars button")[4].Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".outings li").ClassList.Contains("disagree"), Is.True));
    }

    [Test]
    public void One_not_scored_yet_says_so_and_how_to_score_it() =>
        Assert.That(AppStars(null, scored: false), Is.EqualTo("App not scored yet | Not scored yet: run wend outings score"));

    [Test]
    public void One_the_app_cant_rate_shows_a_dash() =>
        Assert.That(AppStars(null), Is.EqualTo("App – | The app can't rate it: too little of it is on the map"));

    //  Clicking the map with the Outings open sets no start (user, 2026-11-09: "I get the orange
    //  dot which is a bit useless here"); back at the Request, it does again.
    private static void ClickTheMap(IRenderedComponent<Planner> screen) =>
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.MapClicked(ParliamentHill.Latitude, ParliamentHill.Longitude)).GetAwaiter().GetResult();

    [Test]
    public void Clicking_the_map_with_the_Outings_open_sets_no_start()
    {
        _outings.List = [HeathRun];
        var screen = OpenOutings();
        ClickTheMap(screen);
        Assert.That(_map.Invocations.Count(i => i.Identifier == "showStart"), Is.Zero);
    }

    [Test]
    public void Back_at_the_Request_clicking_the_map_sets_a_start()
    {
        _outings.List = [HeathRun];
        var screen = OpenOutings();
        screen.Find(".rail button[aria-label='Request']").Click();
        ClickTheMap(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations.Count(i => i.Identifier == "showStart"), Is.GreaterThan(0)));
    }

    [Test]
    public void With_no_Outings_it_says_how_to_import_them() =>
        Assert.That(OpenOutings().Find(".panel").TextContent, Does.Contain("wend outings import-gpx"));

    [Test]
    public void Choosing_an_Outing_draws_its_track_over_the_ground_it_covered()   // AC2
    {
        _outings.List = [HeathRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForAssertion(() =>
        {
            var drawn = (OutingOnMap)_map.Invocations["showOuting"].Single().Arguments[0]!;
            Assert.That((drawn.Track.Count, string.Join(",", drawn.Covered.Select(c => c.Ground)), drawn.Gaps.Count),
                Is.EqualTo((2, "off_road,connector", 2)));
        });
    }

    [Test]
    public void Its_gaps_are_listed_with_their_lengths()   // AC3
    {
        _outings.List = [HeathRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForAssertion(() => Assert.That(
            screen.FindAll(".gaps li").Select(li => li.TextContent.Trim()),
            Is.EqualTo(new[] { "Gap 1 · 142 m", "Gap 2 · 61 m" })));
    }

    //  The user, 2026-11-10: an Outing that can't be made a route ("Route finding in North Mymms
    //  Park") is "precisely the type of route that I want to overlay in OSM to see what's missing":
    //  its own track, every point recorded, as a GPX.
    [Test]
    public void Download_GPX_hands_the_browser_the_Outing_s_own_track()
    {
        var download = _context.JSInterop.SetupModule("./js/download.js");
        download.Mode = JSRuntimeMode.Loose;
        _outings.List = [HeathRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForElement(".outing button.download-gpx").Click();

        var gpx = OutingGpx.Of(HeathRunView.Name, HeathRunView.RouteTrack);
        screen.WaitForAssertion(() => Assert.That(download.Invocations["saveFile"].Single().Arguments, Is.EqualTo(new object?[] { gpx.FileName, "application/gpx+xml", gpx.Content })));
    }

    //  The gaps folded away, so the Outing chosen stays short above the list (user, 2026-11-10).
    [Test]
    public void Its_gaps_are_folded_under_how_many()
    {
        _outings.List = [HeathRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        var gaps = screen.WaitForElement(".outing details.gaps-shown");
        Assert.That((gaps.QuerySelector("summary")!.TextContent.Trim(), gaps.HasAttribute("open")), Is.EqualTo(("Gaps (2)", false)));
    }

    //  A database that stops part-way must leave the screen usable and say so (the Slice 1 review's
    //  rule, missed by the Outings panel until the review after T030).
    [Test]
    public void When_the_Outings_can_t_be_listed_the_panel_says_so()
    {
        _outings.ListFailure = new InvalidOperationException("the database has gone away");
        var screen = OpenOutings();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".panel").TextContent, Does.Contain("Couldn't load your Outings")));
    }

    [Test]
    public void When_an_Outing_can_t_be_shown_the_panel_says_so()
    {
        _outings.List = [HeathRun];
        _outings.ViewFailure = new InvalidOperationException("the database has gone away");
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".panel").TextContent, Does.Contain("Couldn't show that Outing")));
    }

    //  The Outing chosen opens in its own row, not above the list (user, 2026-11-10: "that way it
    //  wouldn't be stuck at the top of the list and somehow not where you expect it").
    [Test]
    public void The_Outing_chosen_opens_in_its_own_row()
    {
        _outings.List = [HeathRun, LakeRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button");
        screen.FindAll(".outings > li > button")[1].Click();
        screen.WaitForAssertion(() => Assert.That(screen.FindAll(".outings > li").Select(li => li.QuerySelector(".outing") is not null), Is.EqualTo(new[] { false, true })));
    }

    [Test]
    public void Choosing_another_moves_it_there()
    {
        _outings.List = [HeathRun, LakeRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button");
        screen.FindAll(".outings > li > button")[1].Click();
        screen.WaitForElement(".outing");
        screen.FindAll(".outings > li > button")[0].Click();
        screen.WaitForAssertion(() => Assert.That(screen.FindAll(".outings > li").Select(li => li.QuerySelector(".outing") is not null), Is.EqualTo(new[] { true, false })));
    }

    //  T106 (user: "I'd like to be able to delete outings", "Gone, never re-imported"): asked first.
    [Test]
    public void Deleting_an_Outing_asks_first()
    {
        _outings.List = [HeathRun, LakeRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForElement(".outing .delete-outing").Click();
        Assert.That((screen.Find(".outing .asking").TextContent.Trim(), _outings.Deleted.Count), Is.EqualTo(("Delete?", 0)));
    }

    [Test]
    public void Yes_deletes_it_and_it_leaves_the_list()
    {
        _outings.List = [HeathRun, LakeRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForElement(".outing .delete-outing").Click();
        screen.Find(".outing .confirm-delete").Click();
        screen.WaitForAssertion(() => Assert.That((string.Join(",", _outings.Deleted), string.Join(",", screen.FindAll(".outings li .name").Select(n => n.TextContent))),
            Is.EqualTo(("7", "Lake Run"))));
    }

    //  The next one shown, so a long list can be gone through one after another.
    [Test]
    public void Deleted_the_next_Outing_is_shown()
    {
        _outings.List = [HeathRun, LakeRun, CanalRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button");
        screen.FindAll(".outings li > button")[1].Click();
        screen.WaitForElement(".outing .delete-outing").Click();
        screen.Find(".outing .confirm-delete").Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".outings > li.on .name").TextContent, Is.EqualTo("Canal Run")));
    }

    //  The last in the list deleted, the one before it is shown.
    [Test]
    public void The_last_deleted_the_one_before_is_shown()
    {
        _outings.List = [HeathRun, LakeRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button");
        screen.FindAll(".outings li > button")[1].Click();
        screen.WaitForElement(".outing .delete-outing").Click();
        screen.Find(".outing .confirm-delete").Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".outings > li.on .name").TextContent, Is.EqualTo("Heath Run")));
    }

    //  T116 (user: "these are split over two lines"): asking, the row is the question's.
    [Test]
    public void Asking_the_other_buttons_step_aside()
    {
        _outings.List = [HeathRun, LakeRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForElement(".outing .delete-outing").Click();
        Assert.That(screen.FindAll(".outing .as-route button").Select(b => b.TextContent.Trim()), Is.EqualTo(new[] { "Yes", "No" }));
    }

    [Test]
    public void No_brings_them_back()
    {
        _outings.List = [HeathRun, LakeRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForElement(".outing .delete-outing").Click();
        screen.Find(".outing .keep").Click();
        Assert.That(screen.FindAll(".outing .as-route button").Select(b => b.TextContent.Trim()), Is.EqualTo(new[] { "To Route", "GPX", "Rename", "Delete" }));
    }

    [Test]
    public void No_keeps_it()
    {
        _outings.List = [HeathRun, LakeRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForElement(".outing .delete-outing").Click();
        screen.Find(".outing .keep").Click();
        Assert.That((_outings.Deleted.Count, screen.FindAll(".outing .delete-outing").Count, screen.FindAll(".outings > li").Count), Is.EqualTo((0, 1, 2)));
    }

    //  The only one deleted: nothing left to show, and it leaves the map.
    [Test]
    public void The_only_one_deleted_leaves_the_map()
    {
        _outings.List = [HeathRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showOuting"], Is.Not.Empty));
        screen.WaitForElement(".outing .delete-outing").Click();
        screen.Find(".outing .confirm-delete").Click();
        screen.WaitForAssertion(() => Assert.That((_map.Invocations["clearOuting"].Count, screen.FindAll(".outing").Count), Is.EqualTo((1, 0))));
    }

    //  T110 (user: "I need a way to filter for my favourite runs (and those yet to be scored) ...
    //  by default I'd see all of them ... and perhaps a name filter"): by the user's own stars.
    private IRenderedComponent<Planner> ListedForFiltering()
    {
        _outings.List = [HeathRun with { UserStars = 5 }, LakeRun with { UserStars = 4 }, CanalRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li");
        return screen;
    }

    private static string Listed(IRenderedComponent<Planner> screen) =>
        string.Join(",", screen.FindAll(".outings > li .name").Select(n => n.TextContent));

    private static void Filter(IRenderedComponent<Planner> screen, string label) =>
        screen.FindAll(".outing-filters button").Single(b => b.TextContent.Trim() == label).Click();

    [Test]
    public void All_are_listed_to_begin_with()
    {
        var screen = ListedForFiltering();
        Assert.That((Listed(screen), screen.Find(".outing-filters button[aria-pressed='true']").TextContent.Trim()),
            Is.EqualTo(("Heath Run,Lake Run,Canal Run", "All")));
    }

    [TestCase("★5", "Heath Run")]
    [TestCase("★4–5", "Heath Run,Lake Run")]
    [TestCase("Not rated", "Canal Run")]
    [TestCase("All", "Heath Run,Lake Run,Canal Run")]
    public void A_filter_lists_only_the_Outings_it_names(string filter, string listed)
    {
        var screen = ListedForFiltering();
        Filter(screen, "★5");
        Filter(screen, filter);
        Assert.That(Listed(screen), Is.EqualTo(listed));
    }

    [Test]
    public void The_name_box_matches_any_part_of_the_name_in_any_case()
    {
        var screen = ListedForFiltering();
        screen.Find(".outing-search").Input("AKE r");
        Assert.That(Listed(screen), Is.EqualTo("Lake Run"));
    }

    [Test]
    public void The_name_box_and_a_filter_narrow_together()
    {
        var screen = ListedForFiltering();
        Filter(screen, "★4–5");
        screen.Find(".outing-search").Input("run");
        Assert.That(Listed(screen), Is.EqualTo("Heath Run,Lake Run"));
    }

    //  T113: how many are listed, beside the filters.
    [Test]
    public void How_many_are_listed_is_shown_beside_the_filters()
    {
        var screen = ListedForFiltering();
        Assert.That(screen.Find(".outing-count").TextContent.Trim(), Is.EqualTo("3 Outings"));
    }

    [Test]
    public void Narrowed_it_says_how_many_of_them_are_listed()
    {
        var screen = ListedForFiltering();
        Filter(screen, "★4–5");
        Assert.That(screen.Find(".outing-count").TextContent.Trim(), Is.EqualTo("2 of 3"));
    }

    [Test]
    public void Narrowed_by_name_it_says_so_too()
    {
        var screen = ListedForFiltering();
        screen.Find(".outing-search").Input("lake");
        Assert.That(screen.Find(".outing-count").TextContent.Trim(), Is.EqualTo("1 of 3"));
    }

    [Test]
    public void One_Outing_is_one()
    {
        _outings.List = [HeathRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li");
        Assert.That(screen.Find(".outing-count").TextContent.Trim(), Is.EqualTo("1 Outing"));
    }

    [Test]
    public void None_matching_says_so()
    {
        var screen = ListedForFiltering();
        screen.Find(".outing-search").Input("Marathon");
        Assert.That((screen.FindAll(".outings > li").Count, screen.Find(".none-match").TextContent.Trim()), Is.EqualTo((0, "No Outings match.")));
    }

    [Test]
    public void The_Outing_shown_stays_shown_if_it_still_matches()
    {
        var screen = ListedForFiltering();
        screen.FindAll(".outings > li > button")[0].Click();
        screen.WaitForElement(".outing");
        Filter(screen, "★5");
        Assert.That(screen.FindAll(".outing").Count, Is.EqualTo(1));
    }

    [Test]
    public void The_Outing_shown_closes_and_leaves_the_map_when_filtered_out()
    {
        var screen = ListedForFiltering();
        screen.FindAll(".outings > li > button")[0].Click();
        screen.WaitForElement(".outing");
        Filter(screen, "Not rated");
        screen.WaitForAssertion(() => Assert.That((screen.FindAll(".outing").Count, _map.Invocations["clearOuting"].Count), Is.EqualTo((0, 1))));
    }

    //  Deleted under a filter, the next shown is the next listed, not one filtered out.
    [Test]
    public void Deleted_under_a_filter_the_next_listed_is_shown()
    {
        _outings.List = [CanalRun, HeathRun with { UserStars = 5 }, LakeRun with { UserStars = 4 }, CanalRun with { Id = 10, Name = "Pond Run", UserStars = 5 }];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li");
        Filter(screen, "★4–5");
        screen.FindAll(".outings > li > button")[0].Click();
        screen.WaitForElement(".outing .delete-outing").Click();
        screen.Find(".outing .confirm-delete").Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".outing").GetAttribute("aria-label"), Is.EqualTo("Lake Run")));
    }

    //  Rated out of the filter, it leaves the list and the map: "Not rated" worked through.
    [Test]
    public void Rated_out_of_the_filter_it_closes()
    {
        var screen = ListedForFiltering();
        Filter(screen, "Not rated");
        screen.FindAll(".outings > li > button")[0].Click();
        screen.WaitForElement(".outing");
        screen.FindAll(".outings > li .my-stars button")[2].Click();
        screen.WaitForAssertion(() => Assert.That((Listed(screen), screen.FindAll(".outing").Count, _map.Invocations["clearOuting"].Count), Is.EqualTo(("", 0, 1))));
    }

    //  T116 (user: "when I delete an outing then the next outing is auto-selected but if I add a
    //  star rating to the outing then this doesn't happen"; chose "When it leaves the list").
    private IRenderedComponent<Planner> NoneRatedShowing(int shown)
    {
        _outings.List = [HeathRun, LakeRun, CanalRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li");
        Filter(screen, "Not rated");
        screen.FindAll(".outings > li > button")[shown].Click();
        screen.WaitForElement(".outing");
        return screen;
    }

    private static void RateShown(IRenderedComponent<Planner> screen, int stars) =>
        screen.FindAll(".outings > li.on .my-stars button")[stars - 1].Click();

    [Test]
    public void Rated_out_of_the_list_the_next_one_is_shown()
    {
        var screen = NoneRatedShowing(0);
        RateShown(screen, 4);
        screen.WaitForAssertion(() => Assert.That((Listed(screen), screen.Find(".outings > li.on .name").TextContent), Is.EqualTo(("Lake Run,Canal Run", "Lake Run"))));
    }

    [Test]
    public void Rated_out_of_the_list_at_its_end_the_one_before_is_shown()
    {
        var screen = NoneRatedShowing(2);
        RateShown(screen, 4);
        screen.WaitForAssertion(() => Assert.That(screen.Find(".outings > li.on .name").TextContent, Is.EqualTo("Lake Run")));
    }

    [Test]
    public void Rated_out_of_the_list_the_next_one_is_drawn_on_the_map()
    {
        var screen = NoneRatedShowing(0);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showOuting"], Has.Count.EqualTo(1)));
        RateShown(screen, 4);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showOuting"], Has.Count.EqualTo(2)));
    }

    [Test]
    public void Rated_and_still_listed_it_stays_shown()
    {
        _outings.List = [HeathRun, LakeRun, CanalRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings > li > button").Click();
        screen.WaitForElement(".outing");
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showOuting"], Has.Count.EqualTo(1)));
        RateShown(screen, 4);
        screen.WaitForAssertion(() => Assert.That(_outings.Rated, Is.Not.Empty));
        Assert.That((screen.Find(".outings > li.on .name").TextContent, _map.Invocations["showOuting"].Count), Is.EqualTo(("Heath Run", 1)));
    }

    //  Another one rated out of the list, the one shown stays shown.
    [Test]
    public void Another_rated_out_of_the_list_the_one_shown_stays()
    {
        var screen = NoneRatedShowing(0);
        screen.FindAll(".outings > li")[1].QuerySelectorAll(".my-stars button")[3].Click();
        screen.WaitForAssertion(() => Assert.That(_outings.Rated, Is.Not.Empty));
        Assert.That((Listed(screen), screen.Find(".outings > li.on .name").TextContent), Is.EqualTo(("Heath Run,Canal Run", "Heath Run")));
    }

    //  T111 (user: "if I have a way to edit the name stored for an outing"): renamed in place, as
    //  a Saved Route is.
    private IRenderedComponent<Planner> RenamingHeathRun()
    {
        _outings.List = [HeathRun, LakeRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings > li > button").Click();
        screen.WaitForElement(".outing .rename-outing").Click();
        return screen;
    }

    //  T113: its button says Save, not a second Rename under the one that opened it.
    [Test]
    public void Renaming_it_is_saved_by_a_Save_button()
    {
        var screen = RenamingHeathRun();
        Assert.That(screen.Find(".outing form button[type='submit']").TextContent.Trim(), Is.EqualTo("Save"));
    }

    [Test]
    public void Rename_offers_its_name_to_edit()
    {
        var screen = RenamingHeathRun();
        Assert.That(screen.Find(".outing .new-name").GetAttribute("value"), Is.EqualTo("Heath Run"));
    }

    [Test]
    public void Renamed_it_keeps_the_new_name_and_lists_it()
    {
        var screen = RenamingHeathRun();
        screen.Find(".outing .new-name").Input("Heath figure of eight");
        screen.Find(".outing form").Submit();
        screen.WaitForAssertion(() => Assert.That((string.Join(",", _outings.Renamed), Listed(screen), screen.Find(".outing").GetAttribute("aria-label")),
            Is.EqualTo(("(7, Heath figure of eight)", "Heath figure of eight,Lake Run", "Heath figure of eight"))));
    }

    [Test]
    public void A_blank_name_is_refused()
    {
        var screen = RenamingHeathRun();
        screen.Find(".outing .new-name").Input("   ");
        screen.Find(".outing form").Submit();
        Assert.That((_outings.Renamed.Count, screen.FindAll(".outing .new-name").Count), Is.EqualTo((0, 1)));
    }

    [Test]
    public void Cancel_keeps_the_old_name()
    {
        var screen = RenamingHeathRun();
        screen.Find(".outing .new-name").Input("Something else");
        screen.Find(".outing .keep-name").Click();
        Assert.That((_outings.Renamed.Count, screen.FindAll(".outing .new-name").Count, Listed(screen)), Is.EqualTo((0, 0, "Heath Run,Lake Run")));
    }

    [Test]
    public void Escape_keeps_the_old_name()
    {
        var screen = RenamingHeathRun();
        screen.Find(".outing .new-name").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.That((_outings.Renamed.Count, screen.FindAll(".outing .new-name").Count), Is.EqualTo((0, 0)));
    }

    private sealed class FakeOutings : IOutingViews
    {
        public IReadOnlyList<OutingSummary> List { get; set; } = [];

        public Exception? ListFailure { get; set; }

        public Exception? ViewFailure { get; set; }

        public Task<IReadOnlyList<OutingSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            ListFailure is null ? Task.FromResult(List) : Task.FromException<IReadOnlyList<OutingSummary>>(ListFailure);

        public Task<OutingView?> ViewAsync(int id, CancellationToken cancellationToken = default) =>
            ViewFailure is not null ? Task.FromException<OutingView?>(ViewFailure)
            : Task.FromResult(id == HeathRunView.Id ? HeathRunView : id == LakeRunView.Id ? LakeRunView : id == CanalRunView.Id ? CanalRunView : null);

        public List<int> Deleted { get; } = [];

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            Deleted.Add(id);
            List = [.. List.Where(o => o.Id != id)];
            return Task.CompletedTask;
        }

        public List<(int Id, string Name)> Renamed { get; } = [];

        public Task RenameAsync(int id, string name, CancellationToken cancellationToken = default)
        {
            Renamed.Add((id, name));
            return Task.CompletedTask;
        }

        public List<(int Id, int? Stars)> Rated { get; } = [];

        public Task RateAsync(int id, int? stars, CancellationToken cancellationToken = default)
        {
            Rated.Add((id, stars));
            return Task.CompletedTask;
        }
    }

    //  A planned Loop and an Outing are never on the map together (user, 2026-10-19: the Loop drawn
    //  over the run they'd chosen looked like a bug). The Loop isn't lost: it comes back with the
    //  Request panel.
    private IRenderedComponent<Planner> PlanALoopThenShowAnOuting()
    {
        _outings.List = [HeathRun];
        var screen = _context.Render<Planner>();
        //  A start clicked on the map finds its Loops (user, 2026-11-03).
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.MapClicked(ParliamentHill.Latitude, ParliamentHill.Longitude)).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"], Is.Not.Empty));
        screen.Find(".rail button[aria-label='Outings']").Click();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showOuting"], Is.Not.Empty));
        return screen;
    }

    [Test]
    public void Showing_an_Outing_takes_the_planned_Loop_off_the_map()
    {
        var screen = PlanALoopThenShowAnOuting();
        screen.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(_map.Invocations["clearLoop"], Is.Not.Empty);
            Assert.That(screen.FindAll(".pager"), Is.Empty);
        }));
    }

    [Test]
    public void Going_back_to_the_Request_brings_the_Loop_back_and_takes_the_Outing_off()
    {
        var screen = PlanALoopThenShowAnOuting();
        screen.Find(".rail button[aria-label='Request']").Click();
        screen.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(_map.Invocations["clearOuting"], Is.Not.Empty);
            Assert.That(_map.Invocations["showLoop"], Has.Count.EqualTo(2));
            Assert.That(screen.Find(".pager").TextContent, Does.Contain("9.7 km"));
        }));
    }

    //  T084: an Outing kept as a route. "Make it a route" under the Outing shown matches its track
    //  for its own Activity as an imported GPX is, and shows it alone, the Outing off the map, its
    //  name offered for saving.
    private static readonly PlannedLoop AsARoute = new(
        Line: [ParliamentHill, KenwoodHouse, new LatLon(51.5650, -0.1500), ParliamentHill],
        Start: ParliamentHill,
        Anchors: [],
        Breakdown: new Breakdown(LengthM: 9_420, OffRoadPercent: 70, TrafficFreePercent: 20, ConnectorLengthM: 900, RetraceLengthM: 0));

    private IRenderedComponent<Planner> MakeTheOutingARoute(int? userStars = null)
    {
        _outings.List = [HeathRun with { UserStars = userStars }];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForElement(".outing .make-route").Click();
        return screen;
    }

    [Test]
    public void An_Outing_made_a_route_is_matched_for_its_own_Activity_and_shown_alone()
    {
        var screen = MakeTheOutingARoute();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(AsARoute)));
        Assert.That((_planner.Imported.Single().Route, _planner.Imported.Single().Activity, _map.Invocations["clearOuting"].Count > 0),
            Is.EqualTo((HeathRunView.RouteTrack, WendYourWay.Core.Loops.Activity.GravelRide, true)));
        Assert.That(screen.Find(".pager .summary").TextContent.Trim(), Is.EqualTo("1 of 1 · 9.4 km"));
    }

    [Test]
    public void The_name_offered_for_saving_it_is_the_Outing_s()
    {
        var screen = MakeTheOutingARoute();
        screen.WaitForElement(".pager .save").Click();
        Assert.That(screen.Find(".pager .save-name").GetAttribute("value"), Is.EqualTo("Heath Run"));
    }

    // T135 (user: "I gave my Trent Park outing a star rating and then converted it to a route and saved it - but the star rating on the saved route has been reset").
    private static void Save(IRenderedComponent<Planner> screen)
    {
        screen.WaitForElement(".pager .save").Click();
        screen.Find(".pager form.saving").Submit();
    }

    [Test]
    public void Its_route_saved_keeps_the_user_s_stars_for_it()
    {
        var screen = MakeTheOutingARoute(userStars: 4);
        Save(screen);
        screen.WaitForAssertion(() => Assert.That(_screen.SavedRoutes.Rated, Is.EqualTo(new Dictionary<int, int?> { [1] = 4 })));
    }

    [Test]
    public void Rated_just_before_its_route_keeps_those_stars()
    {
        _outings.List = [HeathRun];
        var screen = OpenOutings();
        screen.WaitForElement(".outings li button").Click();
        screen.WaitForElement(".outing .make-route");
        screen.FindAll(".outings li .my-stars button")[4].Click();
        screen.WaitForAssertion(() => Assert.That(_outings.Rated, Is.Not.Empty));
        screen.Find(".outing .make-route").Click();
        Save(screen);
        screen.WaitForAssertion(() => Assert.That(_screen.SavedRoutes.Rated, Is.EqualTo(new Dictionary<int, int?> { [1] = 5 })));
    }

    [Test]
    public void Nudged_before_saving_it_s_another_route_saved_unrated()
    {
        _planner.Leaning.Nudged = (_, _) => new NudgedLoop(Planned, 0);
        var screen = MakeTheOutingARoute(userStars: 4);
        screen.WaitForElement(".pager .save");
        screen.InvokeAsync(() => screen.FindComponent<WendYourWay.Web.Components.MapView>().Instance.RouteDragged(51.565, -0.164, 51.566, -0.165)).GetAwaiter().GetResult();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Planned)));
        Save(screen);
        screen.WaitForAssertion(() => Assert.That(_screen.SavedRoutes.Kept, Has.Count.EqualTo(1)));
        Assert.That(_screen.SavedRoutes.Rated, Is.Empty);
    }

    [Test]
    public void Its_stars_taken_off_and_made_a_route_again_it_s_saved_unrated()
    {
        var screen = MakeTheOutingARoute(userStars: 4);
        screen.WaitForElement(".pager .save");
        screen.FindAll(".outings li .my-stars button")[3].Click();
        screen.WaitForAssertion(() => Assert.That(_outings.Rated, Is.EqualTo(new[] { (7, (int?)null) })));
        screen.Find(".outing .make-route").Click();
        Save(screen);
        screen.WaitForAssertion(() => Assert.That(_screen.SavedRoutes.Kept, Has.Count.EqualTo(1)));
        Assert.That(_screen.SavedRoutes.Rated, Is.Empty);
    }

    [Test]
    public void Unrated_its_route_is_saved_unrated()
    {
        var screen = MakeTheOutingARoute();
        Save(screen);
        screen.WaitForAssertion(() => Assert.That(_screen.SavedRoutes.Kept, Has.Count.EqualTo(1)));
        Assert.That(_screen.SavedRoutes.Rated, Is.Empty);
    }

    [Test]
    public void One_that_can_t_be_matched_is_refused_in_the_panel()
    {
        _planner.ImportFailure = new RouteNotMatchedException("Under half of that route follows the map.");
        var screen = MakeTheOutingARoute();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".outing .route-problem").TextContent.Trim(), Is.EqualTo("Under half of that route follows the map.")));
        Assert.That(screen.FindAll(".pager"), Is.Empty);
    }

    private static readonly PlannedLoop Planned = new(
        Line: [ParliamentHill, KenwoodHouse, ParliamentHill],
        Start: ParliamentHill,
        Anchors: [KenwoodHouse, new LatLon(51.5650, -0.1500)],
        Breakdown: new Breakdown(LengthM: 9710, OffRoadPercent: 62, TrafficFreePercent: 17, ConnectorLengthM: 1990, RetraceLengthM: 340));

    private sealed class OneLoopPlanner : ILoopPlanner
    {
        public Task<IOfferedLoops> PlanPointToPointAsync(
            LatLon start, LatLon finish, double? distanceKm = null, WendYourWay.Core.Loops.Leans? leans = null, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IOfferedLoops> ReopenAsync(
            PlannedLoop saved, IReadOnlyList<(WendYourWay.Core.Network.SegmentKey Key, bool Forward)> traversals, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>Each route made from an Outing (T084), and for which Activity.</summary>
        public List<(IReadOnlyList<LatLon> Route, WendYourWay.Core.Loops.Activity Activity)> Imported { get; } = [];

        /// <summary>What a Nudge of the route made gives (T135).</summary>
        public Leaning Leaning { get; } = new();

        /// <summary>When set, making a route fails with it.</summary>
        public Exception? ImportFailure { get; set; }

        public Task<IOfferedLoops> ImportAsync(IReadOnlyList<WendYourWay.Data.LatLon> route, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, CancellationToken cancellationToken = default)
        {
            Imported.Add((route, activity));
            return ImportFailure is null ? Task.FromResult<IOfferedLoops>(new FakeOffer([AsARoute], Leaning)) : Task.FromException<IOfferedLoops>(ImportFailure);
        }

        public Task<IOfferedLoops> PlanAsync(LatLon start, double distanceKm, WendYourWay.Core.Loops.Leans? leans = null, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, Action<PlanningProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IOfferedLoops>(new FakeOffer([Planned]));
    }
}
