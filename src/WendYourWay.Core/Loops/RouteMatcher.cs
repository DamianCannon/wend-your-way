using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;

namespace WendYourWay.Core.Loops;

/// <summary>A route matched onto the Network as a Loop.</summary>
/// <param name="Loop">
/// The route as Segments, in order, from its first waypoint back to it; or, a route that isn't
/// closed (T083), to its last.
/// </param>
/// <param name="LengthM">The matched Loop's length.</param>
/// <param name="Unjoined">
/// Where two waypoints in a row couldn't be joined inside the corridor: the Network has changed
/// there, or the route left it. Not bridged.
/// </param>
public sealed record RouteMatch(Loop Loop, double LengthM, IReadOnlyList<UnjoinedStretch> Unjoined);

/// <summary>A stretch of a route the matcher couldn't join up.</summary>
/// <param name="RoutePoint">Where it starts, by its place in the route's points.</param>
/// <param name="RouteM">How long the route is there.</param>
/// <param name="JoinedM">The shortest way the corridor offered, too long to be the route's; null if none.</param>
/// <param name="From">The slice's junction it couldn't be joined from (T098: to tell why).</param>
/// <param name="To">The slice's junction it couldn't be joined to.</param>
public sealed record UnjoinedStretch(int RoutePoint, double RouteM, double? JoinedM, int From = -1, int To = -1);

/// <summary>
/// Matches a route someone else planned (a Trail Router GPX, say) onto the Network, so it can be
/// scored like a Loop of Wend Your Way's own (increment 1: the user's rated Loops as a spec). Only
/// Segments in the route's corridor can be used, and the further one lies from the route the more
/// it costs, so of a road and the path beside it the one the route took is matched.
/// </summary>
public static class RouteMatcher
{
    // Junctions this close to the route are on it, and can be waypoints.
    private const double OnRouteM = 10;

    // About this far apart along the route: close enough that the way between two is never in
    // doubt, far enough apart that the route finder, not the waypoints, follows the ways.
    private const double WaypointSpacingM = 250;

    // A Segment costs (1 + its mean offset ÷ this) per metre: 1 m off, ×1.5; 8 m off, ×5.
    private const double OffsetScaleM = 2;

    /// <summary>
    /// Where the corridor can't join two junctions on the route, or only the long way round, a
    /// Segment outside it may join them, at this many times its length (T098: of the stretches the
    /// matcher couldn't join in 60 of the user's Outings, most had a short way just outside).
    /// </summary>
    public const double OutsideCorridorPenalty = 3;

    // A closed route's first waypoint, this near its start, may be left out if it can't be joined.
    private const double ClosedStartM = 300;

    // A join this much longer than the route between its two waypoints went the long way round,
    // not the route's way: the Network has changed there. Joined junction by junction instead
    // (T089); what still can't be is reported, not bridged.
    private const double DetourFactor = 1.5;
    private const double DetourSlackM = 50;

    // A join this much shorter than the route between its two waypoints skipped something the
    // route did (an out-and-back up a side street): it's joined junction by junction instead.
    private const double ShortfallShare = 0.1;
    private const double ShortfallSlackM = 30;

    /// <param name="slice">The Network around the route.</param>
    /// <param name="route">The route's points in order (British National Grid), back to its start.</param>
    /// <param name="corridor">Each Segment near the route, by OSM identity, with its mean distance from it.</param>
    /// <param name="closed">
    /// Back to its start (a Loop), or not (a point-to-point route, T083): matched from its first
    /// junction to its last, which is kept however near the waypoint before.
    /// </param>
    public static RouteMatch Match(NetworkSlice slice, IReadOnlyList<NodePoint> route, IReadOnlyDictionary<SegmentKey, double> corridor, bool closed = true)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(corridor);

        var junctions = slice.Segments
            .Where(s => corridor.ContainsKey(s.Key))
            .SelectMany(s => new[] { s.From, s.To })
            .Distinct()
            .ToArray();

        // Each route point's junction, if one lies on the route there, and how far along it is.
        var onRoute = new int[route.Count];
        var alongAt = new double[route.Count];
        for (var i = 0; i < route.Count; i++)
        {
            alongAt[i] = i == 0 ? 0 : alongAt[i - 1] + route[i].DistanceTo(route[i - 1]);
            onRoute[i] = -1;
            if (junctions.Length == 0) continue;
            var nearest = junctions.MinBy(j => slice.Node(j).DistanceTo(route[i]));
            if (slice.Node(nearest).DistanceTo(route[i]) <= OnRouteM) onRoute[i] = nearest;
        }
        var along = route.Count == 0 ? 0 : alongAt[^1];

        // Waypoints: junctions on the route, in the route's order, about every WaypointSpacingM.
        var waypoints = new List<(int Junction, int RoutePoint, double AlongM)>();
        var lastAlong = double.NegativeInfinity;
        for (var i = 0; i < route.Count; i++)
        {
            if (onRoute[i] < 0 || alongAt[i] - lastAlong < WaypointSpacingM) continue;
            if (waypoints.Count == 0 || waypoints[^1].Junction != onRoute[i]) waypoints.Add((onRoute[i], i, alongAt[i]));
            lastAlong = alongAt[i];
        }
        if (waypoints.Count == 0) return new RouteMatch(new Loop(-1, [], []), 0, [new UnjoinedStretch(0, along, null)]);
        var search = new PathSearch(slice);
        var cost = new CorridorCost(corridor);
        var wide = new CorridorCost(corridor, OutsideCorridorPenalty);
        bool Joins(int from, int to, double routeM) =>
            (search.Find(PathEnd.AtNode(from), PathEnd.AtNode(to), cost) ?? search.Find(PathEnd.AtNode(from), PathEnd.AtNode(to), wide)) is { } joined
            && joined.LengthM <= (DetourFactor * routeM) + DetourSlackM;

        if (closed)
        {
            // Where a Loop starts, GPS is still settling: a first junction that can't be joined to
            // the next on the route, within ClosedStartM of the start, is left out, and the Loop
            // begun at the next and closed onto it, the ground between covered as it comes round
            // (2026-11-10: most of the gaps at the ends of routes made from the user's Outings).
            for (var k = waypoints[0].RoutePoint + 1; k < route.Count && alongAt[k] <= ClosedStartM; k++)
            {
                if (onRoute[k] < 0 || onRoute[k] == waypoints[0].Junction) continue;
                if (Joins(waypoints[0].Junction, onRoute[k], alongAt[k] - waypoints[0].AlongM)) break;
                waypoints[0] = (onRoute[k], k, alongAt[k]);
                if (waypoints.Count > 1 && waypoints[1].Junction == waypoints[0].Junction) waypoints.RemoveAt(1);
            }
            // Closed onto the first: as far along again as the first is from the route's start.
            if (waypoints[^1].Junction != waypoints[0].Junction) waypoints.Add((waypoints[0].Junction, route.Count - 1, along + waypoints[0].AlongM));
        }
        else if (Array.FindLastIndex(onRoute, j => j >= 0) is var lastOn && lastOn > waypoints[^1].RoutePoint && onRoute[lastOn] != waypoints[^1].Junction)
        {
            waypoints.Add((onRoute[lastOn], lastOn, alongAt[lastOn]));
        }
        var traversals = new List<Traversal>();
        var unjoined = new List<UnjoinedStretch>();
        var length = 0.0;
        // A stretch not joined, one with the stretch just before it if they meet (T089).
        var unjoinedEndsAt = -1;
        void Unjoin(int routePoint, int endPoint, double routeM, double? joinedM, int fromJunction, int toJunction)
        {
            if (unjoined.Count > 0 && unjoinedEndsAt == routePoint)
            {
                var last = unjoined[^1];
                unjoined[^1] = new UnjoinedStretch(last.RoutePoint, last.RouteM + routeM, last.JoinedM + joinedM, last.From, toJunction);
            }
            else
            {
                unjoined.Add(new UnjoinedStretch(routePoint, routeM, joinedM, fromJunction, toJunction));
            }
            unjoinedEndsAt = endPoint;
        }
        for (var i = 0; i + 1 < waypoints.Count; i++)
        {
            var (from, to) = (waypoints[i], waypoints[i + 1]);
            var routeM = to.AlongM - from.AlongM;
            var path = search.Find(PathEnd.AtNode(from.Junction), PathEnd.AtNode(to.Junction), cost);
            var joined = path is not null && path.LengthM <= (DetourFactor * routeM) + DetourSlackM;
            if (joined && path!.LengthM >= routeM - Math.Max(ShortfallSlackM, ShortfallShare * routeM))
            {
                traversals.AddRange(path.Traversals);
                length += path.LengthM;
                continue;
            }

            // Not joined (T089), or much shorter than the route there: it went somewhere the join
            // skips, such as up a side street and back. Join every junction it passes on the way
            // instead, so only what can't be joined is a gap.
            var previous = from;
            for (var k = from.RoutePoint + 1; k <= to.RoutePoint; k++)
            {
                if (onRoute[k] < 0 || onRoute[k] == previous.Junction) continue;
                var step = search.Find(PathEnd.AtNode(previous.Junction), PathEnd.AtNode(onRoute[k]), cost);
                var stepRouteM = alongAt[k] - previous.AlongM;
                // Not in the corridor: a way just outside it, GPS having wandered or a path drawn a little off (T098).
                if (step is null || step.LengthM > (DetourFactor * stepRouteM) + DetourSlackM)
                    step = search.Find(PathEnd.AtNode(previous.Junction), PathEnd.AtNode(onRoute[k]), wide);
                if (step is null || step.LengthM > (DetourFactor * stepRouteM) + DetourSlackM)
                {
                    Unjoin(previous.RoutePoint, k, stepRouteM, step?.LengthM, previous.Junction, onRoute[k]);
                }
                else
                {
                    traversals.AddRange(step.Traversals);
                    length += step.LengthM;
                }
                previous = (onRoute[k], k, alongAt[k]);
            }
        }
        return new RouteMatch(new Loop(waypoints[0].Junction, [], traversals), length, unjoined);
    }

    // Only the corridor, and dearer the further from the route. Every Segment the route went along
    // is allowed, whatever its Access: the match records what the route did, and scoring judges it.
    private sealed class CorridorCost(IReadOnlyDictionary<SegmentKey, double> corridor, double outside = double.PositiveInfinity) : ICostModel
    {
        public double FloorPerMetre => 1;

        public double CostOf(Segment segment, bool forward) =>
            corridor.TryGetValue(segment.Key, out var offset)
                ? segment.LengthM * (1 + (offset / OffsetScaleM))
                : segment.LengthM * outside;
    }
}
