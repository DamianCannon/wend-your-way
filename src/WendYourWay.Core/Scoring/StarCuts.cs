namespace WendYourWay.Core.Scoring;

/// <summary>
/// The least value for 2, 3, 4 and 5 stars (PLAN T114): a Run's ranking value's, and a ride's score's.
/// Worked out from the user's Outings by <c>wend outings calibrate</c> and kept with their data; until
/// then, the ones measured last (<see cref="InCode"/>).
/// </summary>
public sealed record StarCuts(IReadOnlyList<double> Run, IReadOnlyList<double> Ride)
{
    /// <summary>
    /// A Run's measured 2026-11-11 (T108: the 10th, 30th, 50th and 70th percentiles of 1,202
    /// matched Runs' ranking values), a ride's 2026-11-10 (T098: fifths of 90 rides' scores).
    /// </summary>
    public static StarCuts InCode { get; } = new([0.1910, 0.4008, 0.5575, 0.7172], [0.0059, 0.0198, 0.0457, 0.0845]);

    /// <summary>An Activity with fewer Outings than this keeps the cuts it has.</summary>
    public const int LeastToCalibrate = 20;

    // A Run's cuts half a star above fifths ("add a 0.5 star adjustment", user, T108); a ride's fifths.
    // In whole percents, so floor(q × n) is n × p / 100 exactly (0.7 × 90 in doubles is 62.99…).
    private static readonly int[] RunPercentiles = [10, 30, 50, 70];
    private static readonly int[] RidePercentiles = [20, 40, 60, 80];

    /// <summary>
    /// The cuts from the matched Runs' ranking values and rides' scores: of each sorted, the value at
    /// floor(q × n), as T098 and T108 measured them. An Activity with fewer than
    /// <see cref="LeastToCalibrate"/> keeps <paramref name="kept"/>'s.
    /// </summary>
    public static StarCuts Calibrate(IReadOnlyCollection<double> runRankings, IReadOnlyCollection<double> rideScores, StarCuts kept)
    {
        ArgumentNullException.ThrowIfNull(runRankings);
        ArgumentNullException.ThrowIfNull(rideScores);
        ArgumentNullException.ThrowIfNull(kept);
        return new(Cut(runRankings, RunPercentiles) ?? kept.Run, Cut(rideScores, RidePercentiles) ?? kept.Ride);
    }

    private static double[]? Cut(IReadOnlyCollection<double> values, int[] percents)
    {
        if (values.Count < LeastToCalibrate) return null;
        var sorted = values.Order().ToArray();
        return [.. percents.Select(p => sorted[sorted.Length * p / 100])];
    }

    /// <summary>The cuts kept, each Activity's used only if four rising numbers; else the ones in code.</summary>
    public static StarCuts OrInCode(IReadOnlyList<double>? run, IReadOnlyList<double>? ride) =>
        new(Usable(run) ? run! : InCode.Run, Usable(ride) ? ride! : InCode.Ride);

    private static bool Usable(IReadOnlyList<double>? cuts) =>
        cuts is { Count: 4 } && cuts.All(double.IsFinite) && cuts.Zip(cuts.Skip(1)).All(pair => pair.First <= pair.Second);
}
