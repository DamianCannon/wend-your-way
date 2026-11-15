using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;

namespace WendYourWay.Data.Planning;

/// <summary>
/// The Loops offered for a Request, best first, which can be re-ranked under other Leans from the
/// Loops the Request found, with no new search (T053, R10.4).
/// </summary>
public interface IOfferedLoops : IReadOnlyList<PlannedLoop>
{
    /// <summary>
    /// The Loops offered from the same Request's found Loops at <paramref name="leans"/>, each the
    /// same <see cref="PlannedLoop"/> as when it was offered before.
    /// </summary>
    Task<IOfferedLoops> ReRankAsync(Leans leans, CancellationToken cancellationToken = default);

    /// <summary>
    /// The Loops offered at <paramref name="leans"/> once the Request's slice has been searched at
    /// them too, its Loops added to those found (a background search, T053b). Leans searched
    /// before aren't searched again. How many Loops it has tried goes to
    /// <paramref name="progress"/> as a Request's search does (T054).
    /// </summary>
    Task<IOfferedLoops> SearchAsync(Leans leans, Action<PlanningProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="planned"/>, one of these or a Loop nudged from one, nudged (T076): the
    /// stretch round the Nudge found again at <paramref name="leans"/>, the rest kept; none if
    /// there's no way, or the place isn't on or near the Loop.
    /// </summary>
    Task<NudgedLoop?> NudgeAsync(PlannedLoop planned, LoopNudge nudge, Leans leans, CancellationToken cancellationToken = default);

    /// <summary>
    /// What <paramref name="planned"/>, one of these or a Loop nudged from one, is made of: its
    /// Segments by OSM identity, in travel order, to save it (T080); null if it isn't one.
    /// </summary>
    IReadOnlyList<(SegmentKey Key, bool Forward)>? TraversalsOf(PlannedLoop planned);

    /// <summary>
    /// The app's score for one of these, for its stars when it's saved (T097): with the Default
    /// weights for the Activity it was planned for, as the stars' cut points were; null if it isn't one.
    /// </summary>
    RouteScore? ScoreOf(PlannedLoop planned) => null;

    /// <summary>
    /// The Segment of <paramref name="planned"/> nearest <paramref name="at"/>, within 500 m, by OSM
    /// identity: to Mark it (T082); null if there's none so near, or it isn't one of these.
    /// </summary>
    Task<SegmentKey?> SegmentAtAsync(PlannedLoop planned, LatLon at, CancellationToken cancellationToken = default);

    /// <summary>
    /// For Point-to-points (T091), the shortest way from their start to their end, which each is
    /// labelled against; null for Loops.
    /// </summary>
    double? ShortestM => null;
}
