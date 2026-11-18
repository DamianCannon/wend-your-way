using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;

namespace WendYourWay.Core.Scoring;

/// <summary>
/// The cost of a Segment on a Run, from its ground and the <see cref="ScoringWeights"/>.
/// Ground the Loop has already used (<paramref name="usedGround"/>, by same-ground group) costs
/// more, so later Paths avoid Retracing earlier ones without forbidding it.
/// </summary>
public sealed class RunCostModel(ScoringWeights weights, IReadOnlySet<int>? usedGround = null) : ICostModel
{
    /// <summary>
    /// The steepest relief (metres climbed up and down per metre) the Hills Lean tells apart (T050):
    /// a steady 1 in 10. Steeper ground counts as this steep, both ways.
    /// </summary>
    public const double SteepestRelief = 0.1;

    /// <summary>
    /// What a gap in the map costs (#150, T146a) on top of its ground: as many metres of Off-road, so a
    /// Loop takes one only for a real gain, not to cut a corner.
    /// </summary>
    public const double GapM = 30;

    private readonly IReadOnlySet<int> _usedGround = usedGround ?? new HashSet<int>();

    // Every Segment at the lower of its fresh and its retraced cost (T068), for Landmarks.
    private bool _cheapest;

    /// <summary>
    /// The least each Segment can cost under these weights, whatever ground a Loop has used: the
    /// lower of fresh and retraced, as with a hilly Lean flat ground retraced can cost less than
    /// fresh (T068). <see cref="Routing.Landmarks"/> placed under it guide every Path of a Loop.
    /// </summary>
    public static RunCostModel Cheapest(ScoringWeights weights) => new(weights) { _cheapest = true };

    // The cheapest ground, wholly beside green areas and on a Corridor, sets the floor: Greenery,
    // Corridors and the Off-road Lean leaning to trails are the only adjustments that lower a cost.
    public double FloorPerMetre { get; } = Math.Min(
        weights.OffRoadPerMetre * OffRoadFactor(weights),
        Math.Min(weights.TrafficFreePerMetre, weights.ConnectorPerMetre)) * (1 - weights.GreeneryDiscount) * (1 - weights.CorridorDiscount);

    public double CostOf(Segment segment, bool forward)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (segment.Access == AccessKind.Forbidden) return double.PositiveInfinity;

        // A Main Road's pavement mostly beside water is the river's, not the road's (T045): the
        // Embankment beside the Thames. The carriageway itself stays a Main Road.
        var mainRoad = segment.MainRoad && !(segment.SameGround.Count > 1 && segment.Waterside >= 0.5);
        var groundPerMetre = segment.Ground switch
        {
            GroundKind.OffRoad => weights.OffRoadPerMetre * OffRoadFactor(weights),
            GroundKind.TrafficFree => weights.TrafficFreePerMetre,
            GroundKind.Connector => mainRoad ? weights.MainRoadCostPerMetre : weights.ConnectorPerMetre,
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment.Ground, "Unknown ground."),
        };
        // Never along a Main Road (R2.1): green beside it doesn't make it better.
        var freshPerMetre = groundPerMetre;
        if (!mainRoad) freshPerMetre *= 1 - (weights.GreeneryDiscount * segment.Greenery);
        if (segment.OnCorridor) freshPerMetre *= 1 - weights.CorridorDiscount;
        freshPerMetre *= HillsFactor(segment);
        // Only the metres on ground already used cost the reused-ground rate, on the ground's own
        // rate: discounted, a green Corridor retraced came cheaper than a fresh street (T037 review).
        var retraced = _cheapest
            ? (freshPerMetre <= groundPerMetre * weights.ReusedGroundMultiplier ? 0 : segment.LengthM)
            : segment.RetracedMetres(_usedGround);
        var cost = (freshPerMetre * (segment.LengthM - retraced)) + (groundPerMetre * weights.ReusedGroundMultiplier * retraced);
        // A Crossing costs a fixed amount, shared among its crossing way's Segments by length
        // (T044): as many metres of Off-road as the user would run to avoid it.
        if (segment.Crossing is { } crossing && weights.Crossings is { } crossings)
            cost += weights.OffRoadPerMetre * crossings.Of(crossing, segment.CrossesMainRoad, segment.Busy) * segment.CrossingShare;
        if (segment.MapGap != MapGapKind.None) cost += weights.OffRoadPerMetre * GapM;
        // An Obstacle the same way (T069), on the Activities that pay for them.
        if (segment.Obstacles is { } obstacles && weights.Obstacles is { } obstacleCosts)
        {
            for (var i = 0; i < obstacles.Count; i++)
                cost += weights.OffRoadPerMetre * obstacleCosts.Of(obstacles[i].Kind) * obstacles[i].Share;
        }
        return cost;
    }

    // The Off-road Lean: leaning to tarmac an Off-road metre costs more (T058); leaning to trails,
    // less (T058b), so roads stay as far behind green ground as at the centre.
    private static double OffRoadFactor(ScoringWeights weights) => weights.OffRoadLean < 0
        ? 1 + (-weights.OffRoadLean * weights.OffRoadLeanWeight)
        : 1 - (weights.OffRoadLean * weights.TrailsLeanWeight);

    // The Hills Lean (T050): leaning to fewer hills, a metre costs more by its relief; leaning to
    // more, by how far it falls short of the steepest relief counted. Never cheaper than at the
    // centre, and bounded both ways, so a Lean can't push a Loop off green ground onto roads.
    private double HillsFactor(Segment segment)
    {
        var relief = segment.LengthM > 0 ? Math.Min((segment.AscentM + segment.DescentM) / segment.LengthM, SteepestRelief) : 0;
        return weights.HillsLean < 0
            ? 1 + (-weights.HillsLean * weights.FewerHillsCostWeight * relief)
            : 1 + (weights.HillsLean * weights.MoreHillsCostWeight * (1 - (relief / SteepestRelief)));
    }
}
