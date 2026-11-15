using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;

namespace WendYourWay.Data.Planning;

/// <summary>Plans Loops for a start clicked on the map.</summary>
public interface ILoopPlanner
{
    /// <summary>
    /// Up to five distinct Loops of about <paramref name="distanceKm"/> from <paramref name="start"/>,
    /// best first, leaning as <paramref name="leans"/> say (at their centres if none); none if none
    /// can be made (R1.4).
    /// </summary>
    /// <remarks>
    /// How far it has got goes to <paramref name="progress"/> a few times a second, on the caller's
    /// flow, never a worker thread's: the stage, how many Loops it has tried, and the Loops offered so
    /// far (T054). The Loops it returns are the same either way.
    /// </remarks>
    /// <exception cref="OutsideEnglandAndWalesException">The start is outside England and Wales.</exception>
    /// <exception cref="NetworkNeedsRebuildException">A Gravel Ride on a Network built before them.</exception>
    Task<IOfferedLoops> PlanAsync(
        LatLon start, double distanceKm, Leans? leans = null, Activity activity = Activity.Run, Action<PlanningProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// A route from a GPX (T083) matched onto the Network for <paramref name="activity"/>, offered
    /// alone, to nudge and save as a Loop offered is: closed back to its start if it ends within
    /// 200 m of it, otherwise point to point; its gaps with it.
    /// </summary>
    /// <exception cref="OutsideEnglandAndWalesException">It starts outside England and Wales.</exception>
    /// <exception cref="RouteNotMatchedException">Under half of it runs along what could be matched, or it spreads too far (T086).</exception>
    Task<IOfferedLoops> ImportAsync(IReadOnlyList<LatLon> route, Activity activity = Activity.Run, CancellationToken cancellationToken = default);

    /// <summary>
    /// A Saved Route reopened (T085): <paramref name="saved"/> itself offered alone, on the slice
    /// around it for <paramref name="activity"/>, its Segments found by their OSM identity, to nudge
    /// as an offered Loop is; its line following what was saved where its Segments don't cover it.
    /// </summary>
    /// <exception cref="RouteNotMatchedException">A Segment of it is no longer on the Network, or it spreads too far.</exception>
    Task<IOfferedLoops> ReopenAsync(
        PlannedLoop saved, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, Activity activity = Activity.Run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Point-to-points from <paramref name="start"/> to <paramref name="finish"/> (T090): of about
    /// <paramref name="distanceKm"/>, best first; or, with none, a spread from near the shortest
    /// way to the greenest, shortest first. Nudged and saved as an offered Loop is.
    /// </summary>
    /// <exception cref="OutsideEnglandAndWalesException">The start or end is outside England and Wales.</exception>
    /// <exception cref="PointToPointRefusedException">The Distance is under the shortest way, there's no way, or they're too far apart.</exception>
    Task<IOfferedLoops> PlanPointToPointAsync(
        LatLon start, LatLon finish, double? distanceKm = null, Leans? leans = null, Activity activity = Activity.Run, CancellationToken cancellationToken = default);
}
