using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// The rules a route is chosen by, listed (T200, #204; STORY-053; user: "a list of these rules ... in order of how they
/// get chosen/weighted so that I can see that dirt paths score well except for in certain scenarios"): each ground's rate
/// cheapest first, with what puts a way on it and the exceptions; then what makes a metre cheaper or dearer; one-off costs
/// dearest first; and how whole Loops are judged. Every figure from the weights the planner uses.
/// </summary>
public static class ListingTheRules
{
    private static RuleGroup Group(IReadOnlyList<RuleGroup> rules, string heading) => rules.Single(g => g.Heading == heading);

    private static string[] Lines(RuleGroup group) => [.. group.Rules.Select(r => $"{r.Name}: {r.Figure}")];

    [TestFixture]
    public class The_planner_s_own_weights
    {
        private IReadOnlyList<RuleGroup> _rules = null!;

        [OneTimeSetUp]
        public void Arrange() => _rules = Rules.Of(ScoringWeights.Default);

        [Test]
        public void Ground_is_listed_cheapest_first_each_against_an_Off_road_metre() =>
            Assert.That(Lines(Group(_rules, "What a metre of ground costs")), Is.EqualTo(new[]
            {
                "Off-road: 1×",
                "Traffic-free: 1.1×",
                "Connector: 3×",
                "Main Road with pavements: 6×",
                "Main Road: 15×",
            }));

        //  The user's own case: dirt paths score well, except beside fast roads.
        [Test]
        public void A_dirt_path_is_Off_road_except_beside_a_fast_road() =>
            Assert.That(Group(_rules, "What a metre of ground costs").Rules.Single(r => r.Name == "Off-road").Except, Does.Contain(
                "A dirt path mostly beside roads tagged 50 mph or more (or the national limit) is beside traffic: a Connector. " +
                "Beside slower roads, or ones with no speed on the map, it stays Off-road."));

        [Test]
        public void Beside_a_road_is_within_12_m_or_25_m_of_a_big_one() =>
            Assert.That(Group(_rules, "What a metre of ground costs").Rules.Single(r => r.Name == "Connector").Says, Does.Contain(
                "A path is beside a road when 60% of it lies within 12 m of the road's line, running the same way; " +
                "within 25 m of a dual carriageway or a road of 4 or more lanes."));

        [Test]
        public void The_groups_in_turn() =>
            Assert.That(_rules.Select(g => g.Heading), Is.EqualTo(new[]
            {
                "What a metre of ground costs", "What makes a metre cheaper or dearer", "What a moment costs", "Obstacles, on a Gravel Ride", "How whole Loops are ranked",
            }));

        [Test]
        public void One_off_costs_dearest_first_in_metres_of_Off_road() =>
            Assert.That(Lines(Group(_rules, "What a moment costs")), Is.EqualTo(new[]
            {
                "Crossing a busy road with no crossing: 5,000 m",
                "Crossing a Main Road with no crossing: 3,000 m",
                "Crossing a Main Road at lights: 100 m",
                "Crossing a Main Road at a zebra or marked crossing: 80 m",
                "Crossing a quiet road at lights: 60 m",
                "A gap in the map: 30 m",
                "A way for customers only: 30 m",
                "Crossing a quiet road: 20 m",
            }));

        [Test]
        public void A_Gravel_Ride_s_Obstacles_dearest_first() =>
            Assert.That(Lines(Group(_rules, "Obstacles, on a Gravel Ride"))[..2], Is.EqualTo(new[] { "Stile: 300 m", "Kissing gate: 150 m" }));

        //  Where the search and the ranking differ, both are said: a single figure would mislead.
        [Test]
        public void Ranking_whole_Loops_says_where_it_differs_from_the_search() =>
            Assert.That(Lines(Group(_rules, "How whole Loops are ranked"))[..2], Is.EqualTo(new[] { "Main Road: 3×, not 15×", "Covering ground again: 2×, not 4×" }));
    }

    //  Never out of date: the figures are the weights', whatever they are.
    [TestFixture]
    public class Other_weights
    {
        [Test]
        public void Change_the_figures_and_the_order() =>
            Assert.That(Lines(Group(Rules.Of(ScoringWeights.Default with { ConnectorPerMetre = 0.9 }), "What a metre of ground costs"))[..2],
                Is.EqualTo(new[] { "Connector: 0.9×", "Off-road: 1×" }));
    }
}
