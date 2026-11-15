using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Loops;

/// <summary>
/// The user's Leans for a Request (ADR 0007): each from −1 to +1, 0 its centre. They steer this
/// Request's costs and score only; the weights themselves never change. A Greenery Lean was tried
/// and dropped, as Loops start about as green as the ground allows (T057).
/// </summary>
/// <param name="Hills">Fewer hills (−1) to more (+1), T050.</param>
/// <param name="OffRoad">Tarmac (−1) to trails (+1), T058.</param>
public sealed record Leans(double Hills = 0, double OffRoad = 0)
{
    /// <summary>Every Lean at its centre: the weights as calibrated.</summary>
    public static Leans Centre { get; } = new();

    public double Hills { get; } = Within(Hills, "Hills");

    public double OffRoad { get; } = Within(OffRoad, "Off-road");

    /// <summary>The weights leaning as these Leans do.</summary>
    public ScoringWeights On(ScoringWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        return weights with { HillsLean = Hills, OffRoadLean = OffRoad };
    }

    private static double Within(double lean, string name) =>
        lean is >= -1 and <= 1 ? lean : throw new ArgumentOutOfRangeException(name, lean, $"A {name} Lean must be from -1 to 1.");
}
