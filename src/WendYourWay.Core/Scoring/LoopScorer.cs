using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;

namespace WendYourWay.Core.Scoring;

/// <summary>
/// Scores a Loop per km with the router's cost model (ADR 0006, R2.9, R2.10), but judging the
/// whole Loop as the user does (T042): Main Road links and reused ground at gentler rates than the
/// router charges to steer its Paths, Knots taken off, and raised beside water (T043).
/// </summary>
public static class LoopScorer
{
    // The climbing per km the Hills Lean's score counts in: a hilly run's.
    private const double ClimbPerKmM = 20;

    /// <summary>
    /// The cheapest ground's cost per km divided by the Loop's: 1 for a Loop wholly on bare
    /// Off-road with nothing repeated, more where Greenery and Corridors make ground cheaper, less
    /// for road and Retrace; then raised by its <see cref="Flow"/>, by up to
    /// <see cref="ScoringWeights.FlowWeight"/>. Higher is better; only comparisons under the same
    /// weights mean anything, and the Loop's length alone never changes it.
    /// </summary>
    public static double Score(NetworkSlice slice, Loop loop, ScoringWeights weights)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);

        ArgumentNullException.ThrowIfNull(weights);

        // Walked in order, ground covered earlier costs the reused-ground rate when it comes round
        // again, as the router charges each Path for the Paths before it, at the score's rates.
        var judging = weights with { MainRoadPerMetre = weights.LoopMainRoadPerMetre, ReusedGroundMultiplier = weights.LoopReusedGroundMultiplier };
        var used = new HashSet<int>();
        var model = new RunCostModel(judging, used);
        var cost = 0.0;
        var length = 0.0;
        var water = 0.0;
        var inDestinations = 0.0;
        var climb = 0.0;
        var offRoad = 0.0;
        foreach (var traversal in loop.Traversals)
        {
            var segment = slice.Segments[traversal.Segment];
            // The router never takes a forbidden Segment, but a route the user ran or rated may: it
            // costs what a Main Road does, rather than sinking the whole score to 0 (T037 review).
            cost += model.CostOf(
                segment.Access == AccessKind.Forbidden ? segment with { Ground = GroundKind.Connector, MainRoad = true, Access = AccessKind.Allowed } : segment,
                traversal.Forward);
            length += segment.LengthM;
            water += segment.Waterside * segment.LengthM;
            climb += traversal.Forward ? segment.AscentM : segment.DescentM;
            if (segment.Ground == GroundKind.OffRoad) offRoad += segment.LengthM;
            if (weights.DestinationWeight > 0 && DestinationLoops.InDestination(slice, segment)) inDestinations += segment.LengthM;
            segment.Cover(used);
        }
        // Against the cheapest ground, not the router's floor (which allows for Greenery): 1 is
        // still a Loop wholly on bare Off-road, and green ground scores above it.
        var cheapestGround = Math.Min(weights.OffRoadPerMetre, Math.Min(weights.TrafficFreePerMetre, weights.ConnectorPerMetre));
        var flowShare = length > 0 ? Flow.Of(slice, loop) / length : 0;
        var waterShare = length > 0 ? water / length : 0;
        return cheapestGround * length / cost
            * (1 + (weights.FlowWeight * flowShare))
            * (1 + (weights.WaterWeight * waterShare))
            * (1 + (weights.DestinationWeight * (length > 0 ? inDestinations / length : 0)))
            * Math.Pow(1 - weights.KnotPenalty, Knots.Of(slice, loop))
            // The Hills Lean (T050): bounded both ways, and 1 at its centre.
            * Math.Exp(weights.HillsLean * weights.HillsScoreWeight * (length > 0 ? climb / length * 1000 / ClimbPerKmM : 0))
            // What the user's stars weigh beyond the score (T105): its share off-road and its
            // climbing per km, added; nothing by default.
            + (length > 0 ? (weights.OffRoadShareWeight * offRoad / length) + (weights.ClimbPerKmWeight * climb / length * 1000) : 0);
    }
}
