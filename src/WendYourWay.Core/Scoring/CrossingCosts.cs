using WendYourWay.Core.Network;

namespace WendYourWay.Core.Scoring;

/// <summary>
/// What crossing a road costs, by how it's controlled and whether it's a Main Road, in metres of
/// Off-road the user would run to avoid it (CONTEXT.md: Crossing; PLAN T044).
/// </summary>
public sealed record CrossingCosts(
    double QuietUnmarked, double QuietMarked, double QuietSignals,
    double MainRoadUnmarked, double MainRoadMarked, double MainRoadSignals, double BusyUnmarked = 5000)
{
    /// <summary>
    /// The user's (2026-10-23): waiting at lights costs, and an unmarked Main Road crossing most of all; and
    /// (2026-11-15, T145) "generally I don't like sprinting across main roads", and a busy one with no crossing
    /// "I would NEVER choose": an unmarked Main Road crossing went from 300 to 3,000 (2026-11-16: "about 100 m" along the road
    /// to reach a crossing, which costs 15 a metre each way), a busy road's is 5,000, finite so that with no other way there's still a route.
    /// </summary>
    public static CrossingCosts Default { get; } = new(
        QuietUnmarked: 20, QuietMarked: 20, QuietSignals: 60,
        MainRoadUnmarked: 3000, MainRoadMarked: 80, MainRoadSignals: 100, BusyUnmarked: 5000);

    /// <summary>The metres a whole Crossing of this kind costs; of a busy Main Road, unmarked, the most.</summary>
    public double Of(CrossingKind kind, bool mainRoad, bool busy = false) => (kind, mainRoad) switch
    {
        (CrossingKind.Unmarked, true) when busy => BusyUnmarked,
        (CrossingKind.Signals, true) => MainRoadSignals,
        (CrossingKind.Marked, true) => MainRoadMarked,
        (_, true) => MainRoadUnmarked,
        (CrossingKind.Signals, false) => QuietSignals,
        (CrossingKind.Marked, false) => QuietMarked,
        _ => QuietUnmarked,
    };
}
