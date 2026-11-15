using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// Five Loops to choose from (PLAN T052, R1.4): the best, then each next best that shares no more
/// than 30% of its ground with any already offered and heads at least 45° away from each, if it's
/// good enough.
/// </summary>
public class OfferingFiveLoops
{
    //  2 km square of paths, a junction every 100 m; a 4 km Run from near the middle.
    private static readonly NetworkSlice Grid = GridNetwork.Create(junctionsAcross: 21);
    private static readonly Request FourKm = new(new NodePoint(1030, 970), DistanceKm: 4);

    [Test]
    public void The_first_Loop_offered_is_the_one_the_generator_makes() =>
        Assert.That(LoopGenerator.Offer(Grid, FourKm, ScoringWeights.Default)[0].Traversals,
            Is.EqualTo(LoopGenerator.Generate(Grid, FourKm, ScoringWeights.Default)!.Traversals));

    [Test]
    public void On_a_grid_of_paths_five_Loops_are_offered() =>
        Assert.That(LoopGenerator.Offer(Grid, FourKm, ScoringWeights.Default), Has.Count.EqualTo(5));

    [Test]
    public void No_two_Loops_offered_share_more_than_30_percent_of_their_ground()
    {
        var offered = LoopGenerator.Offer(Grid, FourKm, ScoringWeights.Default);
        var pairs = from a in offered from b in offered where a != b select SharedGround.Share(Grid, a, b);
        Assert.That(pairs, Is.All.LessThanOrEqualTo(0.3));
    }

    //  "Make sure that the different routes go in different directions" (user, 2026-10-25).
    [Test]
    public void The_Loops_offered_head_at_least_45_degrees_apart()
    {
        var headings = LoopGenerator.Offer(Grid, FourKm, ScoringWeights.Default).Select(l => LoopHeading.Of(Grid, l)).OfType<double>().ToList();
        var apart = from a in headings from b in headings where a != b select Math.Min(Math.Abs(a - b), 360 - Math.Abs(a - b));
        Assert.That(apart, Is.All.GreaterThanOrEqualTo(45));
    }

    [Test]
    public void At_most_one_Loop_offered_goes_round_its_start() =>
        Assert.That(LoopGenerator.Offer(Grid, FourKm, ScoringWeights.Default).Count(l => LoopHeading.Of(Grid, l) is null), Is.LessThanOrEqualTo(1));

    [Test]
    public void The_Loops_offered_come_best_first()
    {
        var scores = LoopGenerator.Offer(Grid, FourKm, ScoringWeights.Default).Select(l => LoopScorer.Score(Grid, l, ScoringWeights.Default.Ranking(Activity.Run)));
        Assert.That(scores, Is.Ordered.Descending);
    }

    //  AC8: a square of four blocks has only so many distinct Loops of 1 km or so.
    [Test]
    public void Where_fewer_distinct_Loops_exist_fewer_are_offered()
    {
        var small = GridNetwork.Create(junctionsAcross: 3, blockM: 200);
        Assert.That(LoopGenerator.Offer(small, new Request(new NodePoint(200, 200), DistanceKm: 1.6), ScoringWeights.Default), Has.Count.InRange(1, 4));
    }

    [Test]
    public void Every_Loop_offered_is_within_the_length_window() =>
        Assert.That(LoopGenerator.Offer(Grid, FourKm, ScoringWeights.Default).Select(l => Breakdown.Of(Grid, l).LengthM),
            Is.All.InRange(FourKm.ShortestKm * 1000, FourKm.LongestKm * 1000));
}

/// <summary>
/// Being different is no reason to offer a poor Loop (PLAN T052, the user's choice): after the first,
/// a Loop is offered only if it scores at least half as well and takes no more than 200 m more Main
/// Road ("I never want to run along a main road").
/// </summary>
public class OfferingOnlyGoodLoops
{
    //  The western half of the grid is Off-road, the eastern half Main Roads, the start on the line
    //  between them: Loops into the east are distinct, and worse.
    private static readonly NetworkSlice Grid = MainRoadsEast();
    private static readonly Request FourKm = new(new NodePoint(1000, 970), DistanceKm: 4);

    private static NetworkSlice MainRoadsEast()
    {
        var grid = GridNetwork.Create(junctionsAcross: 21, ground: m => m.X < 1000 ? GroundKind.OffRoad : GroundKind.Connector);
        return new NetworkSlice(
            [.. Enumerable.Range(0, grid.NodeCount).Select(grid.Node)],
            [.. grid.Segments.Select(s => s.Ground == GroundKind.Connector ? s with { MainRoad = true } : s)]);
    }

    private static LoopsOffered Offered() => LoopGenerator.Offer(Grid, FourKm, ScoringWeights.Default);

    [Test]
    public void Every_Loop_offered_on_the_usual_bar_scores_at_least_half_as_well_as_the_first()
    {
        var offered = Offered();
        var scores = offered.Take(offered.OnTheBar).Select(l => LoopScorer.Score(Grid, l, ScoringWeights.Default.Ranking(Activity.Run))).ToList();
        Assert.That(scores, Is.All.GreaterThanOrEqualTo(0.5 * scores[0]));
    }

    [Test]
    public void No_Loop_offered_on_the_usual_bar_takes_more_than_200_m_more_Main_Road_than_the_first()
    {
        var offered = Offered();
        var mainRoad = offered.Take(offered.OnTheBar).Select(l => Breakdown.Of(Grid, l).MainRoadLengthM).ToList();
        Assert.That(mainRoad, Is.All.LessThanOrEqualTo(mainRoad[0] + 200));
    }

    //  Ordinary roads but for one strip of Off-road 400 m wide: no Main Road, but every Loop
    //  that leaves the strip scores far worse.
    [Test]
    public void Beside_ordinary_roads_every_Loop_offered_on_the_usual_bar_scores_at_least_half_as_well_as_the_first()
    {
        var roads = GridNetwork.Create(junctionsAcross: 21, ground: m => m.X is > 600 and < 1000 ? GroundKind.OffRoad : GroundKind.Connector);
        var offered = LoopGenerator.Offer(roads, FourKm, ScoringWeights.Default);
        var scores = offered.Take(offered.OnTheBar).Select(l => LoopScorer.Score(roads, l, ScoringWeights.Default.Ranking(Activity.Run))).ToList();
        Assert.That(scores, Is.All.GreaterThanOrEqualTo(0.5 * scores[0]));
    }

    //  Some are still offered: the west has distinct Loops of its own.
    [Test]
    public void Good_distinct_Loops_are_still_offered() =>
        Assert.That(Offered(), Has.Count.GreaterThan(1));
}

/// <summary>
/// Other directions when few Loops are offered (PLAN T096, STORY-044; user: "add a route that falls
/// below your threshold in a direction not covered by the routes you're suggesting"): while fewer
/// than five are on the usual bar, the next best heading where none offered goes are added after
/// them, if they score at least 0.3 of the first's ("At least 0.3 of the first") and take no more
/// than 2 km more Main Road ("Up to 2 km more").
/// </summary>
public class OfferingOtherDirections
{
    private static readonly Request FourKm = new(new NodePoint(1000, 970), DistanceKm: 4);

    //  Ordinary roads but for one strip of Off-road 400 m wide west of the start: two Loops on the
    //  usual bar; elsewhere, Loops scoring 0.49, 0.44 and 0.29 of the first.
    private static readonly NetworkSlice Strip = GridNetwork.Create(junctionsAcross: 21, ground: m => m.X is > 600 and < 1000 ? GroundKind.OffRoad : GroundKind.Connector);

    //  Off-road to the west, roads to the east: Main Roads from the start's line eastward, where the
    //  best Loop heading east takes 2.6 km more Main Road than the first; or from 500 m east of it,
    //  where one takes about 0.5 km more.
    private static readonly NetworkSlice MainRoadsEast = MainRoadsFrom(1000);
    private static readonly NetworkSlice MainRoadsFarEast = MainRoadsFrom(1500);

    private static NetworkSlice MainRoadsFrom(double x)
    {
        var grid = GridNetwork.Create(junctionsAcross: 21, ground: m => m.X < 1000 ? GroundKind.OffRoad : GroundKind.Connector);
        return new NetworkSlice(
            [.. Enumerable.Range(0, grid.NodeCount).Select(grid.Node)],
            [.. grid.Segments.Select(s => grid.Node(s.From).X >= x && grid.Node(s.To).X >= x ? s with { MainRoad = true } : s)]);
    }

    private static LoopsOffered Offer(NetworkSlice slice) => LoopGenerator.Offer(slice, FourKm, ScoringWeights.Default);

    private static IEnumerable<Loop> Added(LoopsOffered offered) => offered.Skip(offered.OnTheBar);

    private static double Score(NetworkSlice slice, Loop loop) => LoopScorer.Score(slice, loop, ScoringWeights.Default.Ranking(Activity.Run));

    [Test]
    public void Where_fewer_than_five_are_on_the_usual_bar_Loops_in_other_directions_are_added_after_them()
    {
        //  Three on the bar since a Run is ranked with the blend (T105): what it adds narrows the
        //  gap between the strip's Loops and the first; two before.
        var offered = Offer(Strip);
        Assert.That((offered.OnTheBar, offered.Count), Is.EqualTo((3, 4)));
    }

    [Test]
    public void Every_Loop_in_another_direction_scores_below_the_usual_bar()
    {
        var offered = Offer(Strip);
        Assert.That(Added(offered).Select(l => Score(Strip, l)), Is.Not.Empty.And.All.LessThan(0.5 * Score(Strip, offered[0])));
    }

    [Test]
    public void Every_Loop_in_another_direction_scores_at_least_0_3_of_the_first()
    {
        var offered = Offer(Strip);
        Assert.That(Added(offered).Select(l => Score(Strip, l)), Is.All.GreaterThanOrEqualTo(0.3 * Score(Strip, offered[0])));
    }

    [Test]
    public void No_Loop_in_another_direction_takes_more_than_2_km_more_Main_Road_than_the_first()
    {
        var offered = Offer(MainRoadsEast);
        Assert.That(Added(offered).Select(l => Breakdown.Of(MainRoadsEast, l).MainRoadLengthM),
            Is.All.LessThanOrEqualTo(Breakdown.Of(MainRoadsEast, offered[0]).MainRoadLengthM + 2000));
    }

    [Test]
    public void A_Loop_in_another_direction_may_take_more_Main_Road_than_the_usual_200_m()
    {
        var offered = Offer(MainRoadsFarEast);
        Assert.That(Added(offered).Select(l => Breakdown.Of(MainRoadsFarEast, l).MainRoadLengthM),
            Has.Some.GreaterThan(Breakdown.Of(MainRoadsFarEast, offered[0]).MainRoadLengthM + 200));
    }

    [Test]
    public void Loops_in_other_directions_still_head_45_degrees_from_every_other()
    {
        var headings = Offer(Strip).Select(l => LoopHeading.Of(Strip, l)).OfType<double>().ToList();
        var apart = from a in headings from b in headings where a != b select Math.Min(Math.Abs(a - b), 360 - Math.Abs(a - b));
        Assert.That(apart, Is.All.GreaterThanOrEqualTo(45));
    }

    [Test]
    public void Loops_in_other_directions_still_share_no_more_than_30_percent_of_their_ground()
    {
        var offered = Offer(Strip);
        Assert.That(from a in offered from b in offered where a != b select SharedGround.Share(Strip, a, b), Is.All.LessThanOrEqualTo(0.3));
    }

    [Test]
    public void Loops_in_other_directions_come_best_first_among_themselves() =>
        Assert.That(Added(Offer(Strip)).Select(l => Score(Strip, l)), Is.Ordered.Descending);

    [Test]
    public void Each_Loop_says_whether_it_is_in_another_direction()
    {
        var offered = Offer(Strip);
        Assert.That(Enumerable.Range(0, offered.Count).Select(offered.InAnotherDirection), Is.EqualTo(new[] { false, false, false, true }));
    }

    //  The grid of paths has five on the usual bar: nothing to add.
    [Test]
    public void Where_five_are_on_the_usual_bar_none_is_added()
    {
        var offered = LoopGenerator.Offer(GridNetwork.Create(junctionsAcross: 21), new Request(new NodePoint(1030, 970), DistanceKm: 4), ScoringWeights.Default);
        Assert.That((offered.OnTheBar, offered.Count), Is.EqualTo((5, 5)));
    }
}

/// <summary>Shared ground (R1.4): by same-ground group, the larger of the two Loops' shares.</summary>
public class MeasuringSharedGround
{
    //  A 400 m square ring, its four sides 100 m each; and a 200 m out-and-back along its first side
    //  and a 100 m spur off its start: half of the second lies on the ring, a quarter of the ring on it.
    private static readonly NetworkSlice Slice = new([new NodePoint(0, 0), new NodePoint(100, 0), new NodePoint(100, 100), new NodePoint(0, 100), new NodePoint(-100, 0)],
    [
        new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0),
        new Segment(new SegmentKey(2, 2, 3), 1, 2, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 1),
        new Segment(new SegmentKey(3, 3, 4), 2, 3, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 2),
        new Segment(new SegmentKey(4, 4, 1), 3, 0, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 3),
        new Segment(new SegmentKey(5, 1, 5), 0, 4, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 4),
    ]);

    private static readonly Loop Ring = new(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, true), new Traversal(3, true)]);
    private static readonly Loop OutAndBack = new(0, [], [new Traversal(0, true), new Traversal(0, false), new Traversal(4, true), new Traversal(4, false)]);

    [Test]
    public void Two_Loops_share_the_larger_of_their_shares_on_each_others_ground() =>
        Assert.That(SharedGround.Share(Slice, Ring, OutAndBack), Is.EqualTo(0.5));

    [Test]
    public void It_doesnt_matter_which_Loop_comes_first() =>
        Assert.That(SharedGround.Share(Slice, OutAndBack, Ring), Is.EqualTo(0.5));
}

/// <summary>
/// A Loop's heading (PLAN T052): the bearing from its start to the middle of its ground, so Loops
/// offered can go in different directions ("material differences", user).
/// </summary>
public class FindingALoopsHeading
{
    //  A 480 m ring of four 120 m paths from the start at (0, 0), lying to its east or north-west, or round it.
    private static (NetworkSlice, Loop) Ring(NodePoint[] corners)
    {
        var slice = new NetworkSlice(corners,
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 120, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0),
            new Segment(new SegmentKey(2, 2, 3), 1, 2, 120, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 1),
            new Segment(new SegmentKey(3, 3, 4), 2, 3, 120, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 2),
            new Segment(new SegmentKey(4, 4, 1), 3, 0, 120, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 3),
        ]);
        return (slice, new Loop(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, true), new Traversal(3, true)]));
    }

    [Test]
    public void A_Loop_lying_east_of_its_start_heads_east()
    {
        var (slice, loop) = Ring([new NodePoint(0, 0), new NodePoint(100, 50), new NodePoint(200, 0), new NodePoint(100, -50)]);
        Assert.That(LoopHeading.Of(slice, loop), Is.EqualTo(90).Within(1e-9));
    }

    [Test]
    public void A_Loop_lying_north_west_of_its_start_heads_north_west()
    {
        var (slice, loop) = Ring([new NodePoint(0, 0), new NodePoint(-100, 0), new NodePoint(-100, 100), new NodePoint(0, 100)]);
        Assert.That(LoopHeading.Of(slice, loop), Is.EqualTo(315).Within(1e-9));
    }

    //  A long thin Loop either side of its start: its middle 10 m away, under a tenth of its 480 m.
    [Test]
    public void A_Loop_going_round_its_start_has_no_heading()
    {
        var (slice, loop) = Ring([new NodePoint(0, 0), new NodePoint(100, 10), new NodePoint(0, 20), new NodePoint(-100, 10)]);
        Assert.That(LoopHeading.Of(slice, loop), Is.Null);
    }
}
