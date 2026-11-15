namespace WendYourWay.Core.Scoring;

/// <summary>
/// The calibrated weights behind both routing and scoring (ADR 0006, ADR 0007). They live in
/// code, are versioned in git, and are guarded by the Reference Loop specs. Leans adjust them per
/// Request; nothing edits them at runtime.
/// </summary>
/// <param name="OffRoadPerMetre">Cost of a metre of Off-road ground: the baseline.</param>
/// <param name="TrafficFreePerMetre">Cost of a metre of Traffic-free ground.</param>
/// <param name="ConnectorPerMetre">Cost of a metre of Connector (a road with traffic).</param>
/// <param name="ReusedGroundMultiplier">
/// How much more a metre costs when a Loop has already covered that same-ground group: the
/// penalty that steers later Paths away from Retracing earlier ones. At least 1.
/// </param>
/// <param name="MainRoadPerMetre">
/// Cost of a metre of Main Road: steep enough that a Loop takes a quiet way round up to about five
/// times as long, and a Main Road only for a short link (R2.1; user, 2026-10-19).
/// </param>
/// <param name="GreeneryDiscount">
/// How much cheaper a metre is wholly beside green areas, as a share of its ground's cost (T036).
/// </param>
/// <param name="CorridorDiscount">
/// How much cheaper a metre on a Corridor is, as a further share of its cost after Greenery (T037).
/// </param>
/// <param name="LoopMainRoadPerMetre">
/// A Main Road's cost per metre when the score judges a whole Loop: an ordinary road's, since the
/// user's very good Loops take a Main Road link to reach somewhere worth it (T042). The router
/// still charges <paramref name="MainRoadPerMetre"/>, to keep Paths off them.
/// </param>
/// <param name="LoopReusedGroundMultiplier">
/// The reused-ground multiplier when the score judges a whole Loop: out and back along a canal is
/// a Loop the user rates good (T042). The router still charges <paramref name="ReusedGroundMultiplier"/>.
/// </param>
/// <param name="WaterWeight">
/// How much a Loop's score rises with its share beside water (T043): a whole-Loop term like
/// <paramref name="FlowWeight"/>; the router already treats water as green through Greenery.
/// </param>
/// <param name="DestinationWeight">
/// How much a Loop's score rises with its share on green ground in destinations (T047): making
/// the most of the place, a whole-Loop term like <paramref name="WaterWeight"/>.
/// </param>
/// <param name="Crossings">What crossing a road costs, by its kind (T044); none if null.</param>
/// <param name="KnotPenalty">The share of a Loop's score each <see cref="Knots"/> takes off (T042).</param>
/// <param name="HillsLean">
/// The user's Hills Lean (T050), from −1 (fewer hills) to +1 (more); 0, its centre, changes nothing.
/// </param>
/// <param name="FewerHillsCostWeight">
/// How much a Segment's relief (climbing up and down per metre) raises its cost under a full Lean
/// to fewer hills: below 17, so the steepest green path never costs more than a flat road.
/// </param>
/// <param name="MoreHillsCostWeight">
/// How much flat ground's cost rises under a full Lean to more hills: below 2, so a flat green
/// path never costs more than a hilly road.
/// </param>
/// <param name="OffRoadShareWeight">
/// Added to a Loop's score for each whole of it off-road (T105): what the user's stars weigh that
/// the score alone doesn't (T104). 0 by default: nothing changes unless it's set.
/// </param>
/// <param name="ClimbPerKmWeight">
/// Added to a Loop's score for each metre it climbs per km (T105), as the user's stars weigh it. 0
/// by default.
/// </param>
/// <param name="HillsScoreWeight">
/// How much a Loop's score moves with its climbing per km under a full Hills Lean: by e to the
/// power of this times its climbing per 20 m a km, up leaning to more hills, down to fewer.
/// </param>
/// <param name="OffRoadLean">
/// The user's Off-road Lean (T058), from −1 (tarmac) to +1 (trails); 0, its centre, changes nothing.
/// </param>
/// <param name="OffRoadLeanWeight">
/// How much more an Off-road metre costs under a full Lean to tarmac, as a share of its rate: below
/// 2, so it never costs more than an ordinary road.
/// </param>
/// <param name="TrailsLeanWeight">
/// How much less an Off-road metre costs under a full Lean to trails, as a share of its rate (T058b):
/// below 1, so it always costs something. Cheaper trails rather than dearer tarmac, so roads stay as
/// far behind green ground as at the centre.
/// </param>
/// <param name="Obstacles">
/// What Obstacles cost, by kind, in Off-road metres (T069): an Activity's, from <see cref="For"/>;
/// none on a Run.
/// </param>
/// <param name="FlowWeight">
/// How much a Loop's score rises with its <see cref="Flow"/>: by this times the share of the Loop
/// in long green stretches. A whole-Loop term: it chooses between candidates, but A* doesn't
/// steer towards it (ADR 0006).
/// </param>
public sealed record ScoringWeights(
    double OffRoadPerMetre,
    double TrafficFreePerMetre,
    double ConnectorPerMetre,
    double ReusedGroundMultiplier,
    double FlowWeight = 0,
    double MainRoadPerMetre = 0,
    double GreeneryDiscount = 0,
    double CorridorDiscount = 0,
    double LoopMainRoadPerMetre = 0,
    double LoopReusedGroundMultiplier = 1,
    double KnotPenalty = 0,
    double WaterWeight = 0,
    CrossingCosts? Crossings = null,
    double DestinationWeight = 0,
    double HillsLean = 0,
    double FewerHillsCostWeight = 0,
    double MoreHillsCostWeight = 0,
    double HillsScoreWeight = 0,
    double OffRoadLean = 0,
    double OffRoadLeanWeight = 0,
    double TrailsLeanWeight = 0,
    ObstacleCosts? Obstacles = null,
    double OffRoadShareWeight = 0,
    double ClimbPerKmWeight = 0)
{
    /// <summary>
    /// These weights for an Activity (T069): a Gravel Ride pays for Obstacles, a Run doesn't. Every
    /// cost model a Request makes is made from them, so each is its Activity's.
    /// </summary>
    public ScoringWeights For(Loops.Activity activity) =>
        this with { Obstacles = activity == Loops.Activity.GravelRide ? ObstacleCosts.GravelRide : null };

    /// <summary>
    /// These weights for ranking an Activity's Loops (T105): its own (<see cref="For"/>), and for a
    /// Run, what the user's stars weigh beyond the score, from T104's fit of the user's rated Runs
    /// (the score, plus 0.380 × its share off-road and 0.0132 × its climbing per km). A ride is
    /// ranked as before: too few rated. A Run's stars are banded from the same ranking value (T108).
    /// </summary>
    public ScoringWeights Ranking(Loops.Activity activity) =>
        activity == Loops.Activity.Run ? For(activity) with { OffRoadShareWeight = 0.380, ClimbPerKmWeight = 0.0132 } : For(activity);

    /// <summary>A Main Road's cost per metre: never below an ordinary Connector's.</summary>
    public double MainRoadCostPerMetre => Math.Max(MainRoadPerMetre, ConnectorPerMetre);

    /// <summary>
    /// Guesses, guarded by the user's rated Loops (T032, T033): Traffic-free nearly as good as
    /// Off-road (a long tarmac greenwalk "is worth a lot", user), and Flow lifting a Loop wholly in
    /// long green stretches by half; a Main Road five times an ordinary road (user, 2026-10-19); a
    /// metre wholly beside green areas 30% cheaper (T036), and one on a Corridor a further 20% (T037).
    /// Judging a whole Loop, a Main Road at a road's rate, reused ground twice its rate and 15% off a
    /// Knot: fitted to pairs of the user's rated Loops from the same start (T042). The Hills Lean's
    /// weights chosen by outcome from 21 starts, keeping the most green ground (T050): a full Lean
    /// raising the steepest path's cost by at most 2 times and flat ground's by 2.5, neither enough
    /// to make a road cheaper than a green path. The Off-road Lean making an Off-road metre up to twice
    /// its rate leaning to tarmac (T058), and as little as half leaning to trails (T058b).
    /// </summary>
    public static ScoringWeights Default { get; } = new(
        OffRoadPerMetre: 1.0,
        TrafficFreePerMetre: 1.1,
        ConnectorPerMetre: 3.0,
        ReusedGroundMultiplier: 4.0,
        FlowWeight: 0.5,
        MainRoadPerMetre: 15.0,
        GreeneryDiscount: 0.3,
        CorridorDiscount: 0.2,
        LoopMainRoadPerMetre: 3.0,
        LoopReusedGroundMultiplier: 2.0,
        KnotPenalty: 0.15,
        WaterWeight: 0.5,
        Crossings: CrossingCosts.Default,
        FewerHillsCostWeight: 10.0,
        MoreHillsCostWeight: 1.5,
        HillsScoreWeight: 0.5,
        OffRoadLeanWeight: 1.0,
        TrailsLeanWeight: 0.5);
}
