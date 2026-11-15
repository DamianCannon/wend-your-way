using WendYourWay.Core.Loops;

namespace WendYourWay.Core.Scoring;

/// <summary>
/// The app's stars for a route, one to five (PLAN T097, STORY-045). A Run's from its ranking value,
/// as its Loops are ranked (T108): its score plus what the user's stars weigh beyond it, its share
/// off-road and its climbing per km; cut at the 10th, 30th, 50th and 70th percentiles of the user's
/// matched Runs ("Fifths of your Outings", user; then "add a 0.5 star adjustment as well ... to
/// match me"). A ride's from its score alone, a fifth of the user's rides each. The cuts are
/// <see cref="StarCuts"/>: as <c>wend outings calibrate</c> last worked them out (T114), else the
/// ones in code.
/// </summary>
public static class Stars
{
    /// <summary>
    /// The user's stars and the app's this far apart or more, and the route stands out (T097: where
    /// the app's judgement and the user's part).
    /// </summary>
    public const int LeastApart = 2;

    /// <summary>Whether the user's stars and the app's are <see cref="LeastApart"/> or more apart; never when either has none.</summary>
    public static bool Disagree(int? user, int? app) => user is { } u && app is { } a && Math.Abs(u - a) >= LeastApart;

    /// <summary>A route matched for less of its length than this has no stars: its score isn't of the route.</summary>
    public const double LeastMatchedShare = 0.8;

    private static readonly ScoringWeights RunRanking = ScoringWeights.Default.Ranking(Activity.Run);

    /// <summary>A Run's ranking value, as its Loops are ranked: its score plus what its shape adds.</summary>
    public static double RankingOf(double score, double offRoadShare, double climbPerKm) =>
        score + (RunRanking.OffRoadShareWeight * offRoadShare) + (RunRanking.ClimbPerKmWeight * climbPerKm);

    /// <summary>
    /// The stars for a route of this <paramref name="activity"/> scoring <paramref name="score"/>,
    /// matched for <paramref name="matchedShare"/> of its length, that much of it off-road and
    /// climbing that much a km (a Run's count, and one without them has none; a ride's don't); null
    /// when it can't be rated.
    /// A Trail Ride, with no Outings yet, has a Gravel Ride's.
    /// </summary>
    /// <param name="cuts">The cut points; none, the ones in code.</param>
    public static int? Of(Activity activity, double score, double matchedShare, double? offRoadShare, double? climbPerKm, StarCuts? cuts = null)
    {
        cuts ??= StarCuts.InCode;
        if (double.IsNaN(score) || matchedShare < LeastMatchedShare) return null;
        if (activity != Activity.Run) return 1 + cuts.Ride.Count(cut => score >= cut);
        if (offRoadShare is null || climbPerKm is null) return null;
        var ranked = RankingOf(score, offRoadShare.Value, climbPerKm.Value);
        return 1 + cuts.Run.Count(cut => ranked >= cut);
    }
}
