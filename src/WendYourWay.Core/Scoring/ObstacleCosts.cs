using WendYourWay.Core.Network;

namespace WendYourWay.Core.Scoring;

/// <summary>
/// What passing an Obstacle costs, by its kind, in metres of Off-road the user would ride to avoid
/// it (CONTEXT.md: Obstacle; PLAN T069), as a Crossing's cost is.
/// </summary>
public sealed record ObstacleCosts(
    double Gate, double CycleBarrier, double KissingGate, double Turnstile,
    double MotorcycleBarrier, double Stile, double CattleGrid, double Ford)
{
    /// <summary>
    /// A Gravel Ride's, guesses to tune by eye (T069): a gate is a stop to open it; a cycle barrier a
    /// slow weave; a kissing gate, turnstile or motorcycle barrier a bike lifted or squeezed through;
    /// a stile a bike lifted over.
    /// </summary>
    public static ObstacleCosts GravelRide { get; } = new(
        Gate: 30, CycleBarrier: 50, KissingGate: 150, Turnstile: 150,
        MotorcycleBarrier: 150, Stile: 300, CattleGrid: 10, Ford: 100);

    /// <summary>The metres a whole Obstacle of this kind costs.</summary>
    public double Of(ObstacleKind kind) => kind switch
    {
        ObstacleKind.Gate => Gate,
        ObstacleKind.CycleBarrier => CycleBarrier,
        ObstacleKind.KissingGate => KissingGate,
        ObstacleKind.Turnstile => Turnstile,
        ObstacleKind.MotorcycleBarrier => MotorcycleBarrier,
        ObstacleKind.Stile => Stile,
        ObstacleKind.CattleGrid => CattleGrid,
        ObstacleKind.Ford => Ford,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Obstacle."),
    };
}
