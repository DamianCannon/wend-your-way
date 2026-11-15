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

/// <summary>The planner page (T119, from AskingForALoop): how far a Request has got, said while it goes on.</summary>
public class WatchingTheSearch : PlannerPageSpecs
{
    private static string Progress(IRenderedComponent<Planner> screen) => screen.Find(".pager .progress .says").TextContent.Trim();

    //  T054: while a Loop is being found, the pager card says how far it has got in real numbers,
    //  the map shows each Loop tried faintly, and the Loops found so far are offered.
    private IRenderedComponent<Planner> FindingWith(params PlanningProgress[] steps)
    {
        _planner.Script.AddRange(steps);
        _planner.Pending = new TaskCompletionSource<PlannedLoop?>();
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        return screen;
    }

    [Test]
    public void Reading_the_map_it_says_how_much_of_it_is_read()
    {
        var screen = FindingWith(new PlanningProgress(PlanningStage.Reading, 412_000, 1_068_400, TimeSpan.FromSeconds(8), null));
        Assert.That(Progress(screen), Is.EqualTo("Reading the map · 412,000 of 1,068,400 paths and roads · about 8 s left"));
    }

    [Test]
    public void Before_the_map_is_counted_it_says_how_much_is_read() =>
        Assert.That(Progress(FindingWith(new PlanningProgress(PlanningStage.Reading, 25_000, null, null, null))),
            Is.EqualTo("Reading the map · 25,000 paths and roads"));

    [Test]
    public void Trying_Loops_it_says_how_many_of_how_many()
    {
        var screen = FindingWith(new PlanningProgress(PlanningStage.Trying, 87, 204, null, null));
        Assert.Multiple(() =>
        {
            Assert.That(Progress(screen), Is.EqualTo("Trying Loops · 87 of 204"));
            Assert.That(screen.Find(".pager .progress .done").GetAttribute("style"), Does.Contain("width: 43%"));
        });
    }

    [Test]
    public void Drawing_them_it_says_so() =>
        Assert.That(Progress(FindingWith(new PlanningProgress(PlanningStage.Drawing, 0, null, null, null))), Is.EqualTo("Drawing the Loops"));

    [Test]
    public void The_Loops_found_so_far_are_offered_while_it_goes_on()
    {
        var screen = FindingWith(new PlanningProgress(PlanningStage.Trying, 120, 204, null, new FakeOffer([Second])));
        Assert.Multiple(() =>
        {
            Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 10.2 km"));
            Assert.That(Progress(screen), Does.StartWith("Trying Loops · 120 of 204"));
            Assert.That(_map.Invocations["showLoop"].Single().Arguments[0], Is.EqualTo(Second));
        });
    }

    [Test]
    public void When_it_s_done_the_progress_goes()
    {
        var screen = FindingWith(new PlanningProgress(PlanningStage.Trying, 1, 204, null, null));
        _planner.Pending!.SetResult(Planned);
        screen.WaitForState(() => screen.FindAll(".pager .summary").Count > 0, Settling);
        Assert.That(screen.FindAll(".pager .progress"), Is.Empty);
    }

    //  "When a final map is shown then pressing Go does not clear the map" (user, 2026-10-26).
    [Test]
    public void Go_takes_the_last_Loop_off_the_map_at_once()
    {
        _planner.Answer = Planned;
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForState(() => screen.FindAll(".pager .summary").Count > 0, Settling);
        _planner.Pending = new TaskCompletionSource<PlannedLoop?>();
        PressGo(screen);
        Assert.That(_map.Invocations["clearLoop"], Is.Not.Empty);
    }

    //  The background search after the slider moves says how far it has got too ("changing the
    //  hill slider says looking for other routes", user).
    [Test]
    public void While_it_searches_it_says_how_many_Loops_it_has_tried()
    {
        _planner.Leaning.SearchGate = new TaskCompletionSource();
        _planner.Leaning.SearchScript.Add(new PlanningProgress(PlanningStage.Trying, 87, 204, null, null));
        var screen = OfferThree();
        Slide(screen, 1);
        UntilSearched();
        screen.WaitForState(() => screen.FindAll(".pager .progress .says").Count > 0, Settling);
        Assert.That(Progress(screen), Is.EqualTo("Looking for more Loops · 87 of 204"));
        _planner.Leaning.SearchGate.SetResult();
    }

    //  The Loops tried were drawn faintly till the user found them "a bit annoying" (2026-10-29).
    [Test]
    public void No_Loop_tried_is_drawn_on_the_map()
    {
        var screen = FindingWith(new PlanningProgress(PlanningStage.Trying, 1, 204, null, null));
        _planner.Pending!.SetResult(Planned);
        screen.WaitForState(() => screen.FindAll(".pager .summary").Count > 0, Settling);
        Assert.That(_map.Invocations.Select(i => i.Identifier), Has.None.Contains("ried"));
    }
}
