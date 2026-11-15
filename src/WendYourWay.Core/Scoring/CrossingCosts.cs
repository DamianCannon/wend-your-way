using WendYourWay.Core.Network;

namespace WendYourWay.Core.Scoring;

/// <summary>
/// What crossing a road costs, by how it's controlled and whether it's a Main Road, in metres of
/// Off-road the user would run to avoid it (CONTEXT.md: Crossing; PLAN T044).
/// </summary>
public sealed record CrossingCosts(
    double QuietUnmarked, double QuietMarked, double QuietSignals,
    double MainRoadUnmarked, double MainRoadMarked, double MainRoadSignals)
{
    /// <summary>The user's (2026-10-23): waiting at lights costs, and an unmarked Main Road crossing most of all.</summary>
    public static CrossingCosts Default { get; } = new(
        QuietUnmarked: 20, QuietMarked: 20, QuietSignals: 60,
        MainRoadUnmarked: 300, MainRoadMarked: 80, MainRoadSignals: 100);

    /// <summary>The metres a whole Crossing of this kind costs.</summary>
    public double Of(CrossingKind kind, bool mainRoad) => (kind, mainRoad) switch
    {
        (CrossingKind.Signals, true) => MainRoadSignals,
        (CrossingKind.Marked, true) => MainRoadMarked,
        (_, true) => MainRoadUnmarked,
        (CrossingKind.Signals, false) => QuietSignals,
        (CrossingKind.Marked, false) => QuietMarked,
        _ => QuietUnmarked,
    };
}
