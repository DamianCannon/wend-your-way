using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Specs.Scoring;

/// <summary>
/// What the user's stars weigh beyond the score (PLAN T105, from T104's fit): a Loop's share
/// off-road and its climbing per km, each added to its score by its own weight; 0 by default, so
/// nothing changes unless they're set.
/// </summary>
public class WeighingOffRoadAndHills
{
    //  A 400 m ring, half Off-road and half Connector, climbing 8 m the way the Loop goes: 20 m per km.
    private static double Score(ScoringWeights weights)
    {
        var slice = new NetworkSlice([new NodePoint(0, 0), new NodePoint(50, 0), new NodePoint(50, 50), new NodePoint(0, 50)],
        [
            new Segment(new SegmentKey(1, 1, 2), 0, 1, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 0, AscentM: 2, DescentM: 0),
            new Segment(new SegmentKey(2, 2, 3), 1, 2, 100, GroundKind.OffRoad, AccessKind.Allowed, SameGroundGroup: 1, AscentM: 2, DescentM: 0),
            new Segment(new SegmentKey(3, 3, 4), 2, 3, 100, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 2, AscentM: 2, DescentM: 0),
            new Segment(new SegmentKey(4, 1, 4), 0, 3, 100, GroundKind.Connector, AccessKind.Allowed, SameGroundGroup: 3, AscentM: 0, DescentM: 2),
        ]);
        var loop = new Loop(0, [], [new Traversal(0, true), new Traversal(1, true), new Traversal(2, true), new Traversal(3, false)]);
        return LoopScorer.Score(slice, loop, weights);
    }

    [Test]
    public void Its_share_off_road_adds_its_weight_times_the_share() =>
        Assert.That(Score(ScoringWeights.Default with { OffRoadShareWeight = 0.4 }) - Score(ScoringWeights.Default), Is.EqualTo(0.4 * 0.5).Within(1e-12));

    [Test]
    public void Its_climbing_adds_its_weight_times_the_metres_per_km() =>
        Assert.That(Score(ScoringWeights.Default with { ClimbPerKmWeight = 0.01 }) - Score(ScoringWeights.Default), Is.EqualTo(0.01 * 20).Within(1e-12));

    [Test]
    public void By_default_they_add_nothing() =>
        Assert.That((ScoringWeights.Default.OffRoadShareWeight, ScoringWeights.Default.ClimbPerKmWeight), Is.EqualTo((0.0, 0.0)));
}
