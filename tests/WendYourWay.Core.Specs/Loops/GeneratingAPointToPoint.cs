using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// A Point-to-point (STORY-037, T090): generated as a Loop is, from a start to an end. With no
/// Distance, a spread from near the shortest way to the greenest; with one, about that long. On a
/// grid of 100 m blocks whose northern rows are paths, its southern streets.
/// </summary>
public class GeneratingAPointToPoint
{
    // Streets along the bottom three rows, paths above: the greener way is a detour north.
    private static readonly NetworkSlice Grid = GridNetwork.Create(junctionsAcross: 21,
        ground: m => m.Y > 250 ? GroundKind.TrafficFree : GroundKind.Connector);

    private static readonly NodePoint From = new(200, 100);
    private static readonly NodePoint To = new(1400, 100);

    private static PointToPoints Find(double? distanceKm = null) =>
        PointToPointGenerator.Find(Grid, new PointToPointRequest(From, To, distanceKm), ScoringWeights.Default);

    private static double LengthOf(Loop loop) => loop.Traversals.Sum(t => Grid.Segments[t.Segment].LengthM);

    private static int EndOf(Loop loop)
    {
        var last = loop.Traversals[^1];
        return last.Forward ? Grid.Segments[last.Segment].To : Grid.Segments[last.Segment].From;
    }

    [Test]
    public void Each_route_goes_from_the_start_to_the_end()
    {
        var found = Find();
        Assert.That(found.Offered.Select(l => (Grid.Node(l.Start), Grid.Node(EndOf(l)))).Distinct(), Is.EqualTo(new[] { (From, To) }));
    }

    //  1,200 m along the streets is the shortest way: a 1 km Distance can't be.
    [Test]
    public void A_Distance_under_the_shortest_way_is_refused_with_it()
    {
        var found = Find(distanceKm: 1);
        Assert.That((found.ShortestM, found.Offered.Count), Is.EqualTo(((double?)1200, 0)));
    }

    private static (double LengthM, double Score)[] Measured(PointToPoints found)
    {
        var weights = ScoringWeights.Default.For(Activity.Run);
        return [.. found.Offered.Select(l => (LengthOf(l), LoopScorer.Score(Grid, l, weights)))];
    }

    //  The first near the shortest way, then each a step longer and scoring better, none past
    //  1.5 times the shortest.
    [Test]
    public void With_no_Distance_the_spread_climbs_in_steps_from_near_the_shortest()
    {
        var spread = Measured(Find());
        Assume.That(spread, Has.Length.GreaterThanOrEqualTo(2), "a spread to judge");
        Assert.That((
                spread[0].LengthM <= 1.1 * 1200,
                spread.Zip(spread.Skip(1)).All(p => p.Second.LengthM >= 1.1 * p.First.LengthM && p.Second.Score > p.First.Score),
                spread[^1].LengthM <= 1.5 * 1200),
            Is.EqualTo((true, true, true)));
    }

    [Test]
    public void With_no_Distance_there_is_a_spread_to_choose_from()
    {
        Assert.That(Find().Offered, Has.Count.GreaterThanOrEqualTo(2));
    }

    //  Its last the greenest: more of it on the paths to the north than the first.
    [Test]
    public void The_spread_ends_greener_than_it_starts()
    {
        var spread = Find().Offered;
        Assume.That(spread, Has.Count.GreaterThanOrEqualTo(2), "a spread to judge");
        Assert.That(Breakdown.Of(Grid, spread[^1]).TrafficFreePercent, Is.GreaterThan(Breakdown.Of(Grid, spread[0]).TrafficFreePercent + 15));
    }

    //  A Loop's window: 1 km under to 1 km over 1.8 km.
    [Test]
    public void With_a_Distance_each_is_within_a_Loop_s_window_of_it()
    {
        var found = Measured(Find(distanceKm: 1.8));
        Assert.That((found.Length > 0, found.All(f => f.LengthM is >= 800 and <= 2800)), Is.EqualTo((true, true)));
    }

    //  3 km: the 1.2 km shortest way is too short to offer.
    [Test]
    public void With_a_longer_Distance_nothing_short_of_its_window_is_offered()
    {
        var found = Measured(Find(distanceKm: 3));
        Assert.That((found.Length > 0, found.All(f => f.LengthM is >= 2000 and <= 4000)), Is.EqualTo((true, true)));
    }

    //  Where the straight way is the green one, a 3 km Distance still isn't offered it.
    [Test]
    public void With_a_Distance_a_better_way_too_short_is_not_offered()
    {
        var green = GridNetwork.Create(junctionsAcross: 21, ground: m => m.Y > 250 ? GroundKind.Connector : GroundKind.TrafficFree);
        var found = PointToPointGenerator.Find(green, new PointToPointRequest(From, To, 3), ScoringWeights.Default).Offered;
        Assert.That((found.Count > 0, found.All(l => l.Traversals.Sum(t => green.Segments[t.Segment].LengthM) >= 2000)), Is.EqualTo((true, true)));
    }

    //  On routes made up for it, by length and score (the shortest way 1,000 m): from the best
    //  near the shortest, past one only 5% longer, to the best a step on, to the next.
    [Test]
    public void The_spread_steps_past_routes_hardly_longer()
    {
        var (near, best, hardlyLonger, step, next, tooLong) = (new Loop(1, [], []), new Loop(2, [], []), new Loop(3, [], []), new Loop(4, [], []), new Loop(5, [], []), new Loop(6, [], []));
        var spread = PointToPointGenerator.Spread(
            [(near, 1050, 1.0), (best, 1080, 1.1), (hardlyLonger, 1130, 1.3), (step, 1200, 1.2), (next, 1400, 1.4), (tooLong, 1600, 2.0)], 1000);
        Assert.That(spread, Is.EqualTo(new[] { best, step, next }));
    }

    //  Each different enough from the others to be worth offering, as a Request's Loops are.
    [Test]
    public void With_a_Distance_none_shares_much_ground_with_another()
    {
        var found = Find(distanceKm: 1.8).Offered;
        Assert.That(found.SelectMany((a, i) => found.Skip(i + 1).Select(b => SharedGround.Share(Grid, a, b))).All(s => s <= LoopGenerator.MostShared), Is.True);
    }
}
