using AngleSharp.Dom;
using Bunit;
using WendYourWay.Core.Scoring;
using WendYourWay.Web.Components;
using WendYourWay.Web.Components.Pages;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The rules a route is chosen by (T200, #204; STORY-053; user: "I'd like to be able to see a list of these rules";
/// chose "a panel and also a section in the page"): one list, from the planner's own weights, in a Rules panel off the
/// rail and on How it works.
/// </summary>
public class ReadingTheRules : PlannerPageSpecs
{
    // Each rule as it reads: its name, its figure, what it says and its exceptions, group by group.
    private static string[] Listed(IElement rules) =>
        [.. rules.QuerySelectorAll("h3, li.rule").Select(e => string.Join(" | ", e.Matches("h3")
            ? [e.TextContent.Trim()]
            : e.QuerySelectorAll(".name, .figure, .says, .except li").Select(x => x.TextContent.Trim())))];

    private static string[] Expected() =>
        [.. Rules.Of(ScoringWeights.Default).SelectMany(g => new[] { g.Heading }.Concat(g.Rules.Select(r =>
            string.Join(" | ", new[] { r.Name, r.Figure, r.Says }.Where(s => s.Length > 0).Concat(r.Except)))))];

    [Test]
    public void The_Rules_panel_lists_them_all()
    {
        var screen = OfferThree();
        screen.Find(".rail button[aria-label='Rules']").Click();
        Assert.That(Listed(screen.Find(".panel .rules")), Is.EqualTo(Expected()));
    }

    [Test]
    public void How_it_works_lists_the_same()
    {
        using var context = new BunitContext();
        Assert.That(Listed(context.Render<HowItWorks>().Find("section.chosen .rules")), Is.EqualTo(Expected()));
    }

    //  The user's own case, as it reads.
    [Test]
    public void A_dirt_path_s_exception_is_beside_it()
    {
        using var context = new BunitContext();
        var offRoad = context.Render<RulesList>().FindAll("li.rule").Single(r => r.QuerySelector(".name")!.TextContent == "Off-road");
        Assert.That(offRoad.QuerySelectorAll(".except li").Select(li => li.TextContent), Has.Some.StartsWith("A dirt path mostly beside roads tagged 50 mph"));
    }
}
