using Bunit;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The planner page: Wend It! (T194, #198; STORY-052; user: "a 'Wend It!' button that comes up with variations on the
/// route"). On a route shown alone (imported, made from an Outing, or a Saved Route), it offers the route's variations
/// with less road, paged as Loops are, each saying what it gains; with none, it says so and the route stays.
/// </summary>
public class WendingARoute : PlannerPageSpecs
{
    private static readonly PlannedLoop Wended = Third with { Wended = new Wended(1_240, 0.67) };
    private static readonly PlannedLoop AlsoWended = Planned with { Wended = new Wended(600, 0.81) };

    private Bunit.IRenderedComponent<WendYourWay.Web.Components.Pages.Planner> Imported()
    {
        _planner.Imported = Second;
        var screen = OfferThree();
        Import(screen, HeathGpx);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
        return screen;
    }

    [Test]
    public void A_route_shown_alone_can_be_wended() =>
        Assert.That(Imported().FindAll(".pager .wend-it"), Has.Count.EqualTo(1));

    //  After Explain? on the bar (T198, #202; user: "maybe move the wend it button back next to explain"); the card keeps
    //  its width as the downloads are under the bar.
    [Test]
    public void It_sits_after_Explain() =>
        Assert.That(Imported().FindAll(".pager .bar .explain + .wend-it"), Has.Count.EqualTo(1));

    //  Loops offered for a Request are already the planner's best: no Wend It! on them.
    [Test]
    public void Loops_offered_for_a_Request_cannot() =>
        Assert.That(OfferThree().FindAll(".pager .wend-it"), Is.Empty);

    [Test]
    public void Wended_its_variations_are_offered_and_the_first_shown()
    {
        _planner.Leaning.WendIt = _ => [Wended, AlsoWended];
        var screen = Imported();
        screen.Find(".pager .wend-it").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Wended)));
        Assert.That((_planner.Leaning.Wended.Single(), Pager(screen)), Is.EqualTo((Second, "1 of 2 · 9.3 km")));
    }

    [Test]
    public void Each_says_what_it_gains()
    {
        _planner.Leaning.WendIt = _ => [Wended, AlsoWended];
        var screen = Imported();
        screen.Find(".pager .wend-it").Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .wended").TextContent.Trim(), Is.EqualTo("Wended: 1.2 km less road, 67% of your route kept")));
    }

    //  Its variations aren't wended again: Undo goes back to the route.
    [Test]
    public void A_variation_cannot_be_wended_again()
    {
        _planner.Leaning.WendIt = _ => [Wended];
        var screen = Imported();
        screen.Find(".pager .wend-it").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Wended)));
        Assert.That(screen.FindAll(".pager .wend-it"), Is.Empty);
    }

    [Test]
    public void Undo_goes_back_to_the_route()
    {
        _planner.Leaning.WendIt = _ => [Wended];
        var screen = Imported();
        screen.Find(".pager .wend-it").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Wended)));
        screen.Find(".pager .undo").Click();
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"][^1].Arguments[0], Is.EqualTo(Second)));
    }

    [Test]
    public void With_no_better_way_it_says_so_and_the_route_stays()
    {
        var screen = Imported();
        screen.Find(".pager .wend-it").Click();
        screen.WaitForAssertion(() => Assert.That(screen.Find(".pager .nudge-note").TextContent.Trim(),
            Is.EqualTo("Already about as wended as it gets: no way with less road keeps its shape")));
        Assert.That(Pager(screen), Is.EqualTo("1 of 1 · 10.2 km"));
    }
}
