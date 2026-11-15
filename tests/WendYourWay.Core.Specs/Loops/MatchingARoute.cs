using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Specs.Support;

namespace WendYourWay.Core.Specs.Loops;

/// <summary>
/// A route someone else planned (a Trail Router GPX), matched onto the Network as a Loop so it can
/// be scored like Wend Your Way's own (increment 1: the user's rated Loops as a spec). The Data layer
/// supplies the corridor: each Segment near the route and how far, on average, it lies from it.
/// On a grid of 100 m blocks.
/// </summary>
public class MatchingARoute
{
    //  Points every 10 m along straight legs between grid corners (column, row).
    private static NodePoint[] Route(params (int Column, int Row)[] corners)
    {
        var points = new List<NodePoint>();
        for (var i = 0; i + 1 < corners.Length; i++)
        {
            var (from, to) = (corners[i], corners[i + 1]);
            var steps = (int)(Math.Max(Math.Abs(to.Column - from.Column), Math.Abs(to.Row - from.Row)) * 10);
            for (var s = 0; s < steps; s++)
            {
                var t = (double)s / steps;
                points.Add(new NodePoint(100 * (from.Column + ((to.Column - from.Column) * t)), 100 * (from.Row + ((to.Row - from.Row) * t))));
            }
        }
        points.Add(new NodePoint(100 * corners[^1].Column, 100 * corners[^1].Row));
        return [.. points];
    }

    //  As the Data layer measures it: each Segment's mean distance from the route, if within 20 m.
    private static Dictionary<SegmentKey, double> Corridor(NetworkSlice slice, NodePoint[] route)
    {
        double DistanceToRoute(NodePoint p) => Enumerable.Range(0, route.Length - 1).Min(i => DistanceToPiece(p, route[i], route[i + 1]));
        var corridor = new Dictionary<SegmentKey, double>();
        foreach (var segment in slice.Segments)
        {
            var (a, b) = (slice.Node(segment.From), slice.Node(segment.To));
            var offset = Enumerable.Range(0, 11)
                .Average(i => DistanceToRoute(new NodePoint(a.X + ((b.X - a.X) * i / 10.0), a.Y + ((b.Y - a.Y) * i / 10.0))));
            if (offset <= 20) corridor[segment.Key] = offset;
        }
        return corridor;
    }

    // The grid without these ways, either way round: the Network no longer has them.
    private static NetworkSlice Without(NetworkSlice grid, params (NodePoint A, NodePoint B)[] gone) =>
        new([.. Enumerable.Range(0, grid.NodeCount).Select(grid.Node)],
            [.. grid.Segments.Where(s => !gone.Any(g => (grid.Node(s.From), grid.Node(s.To)) == (g.A, g.B) || (grid.Node(s.From), grid.Node(s.To)) == (g.B, g.A)))]);

    private static double DistanceToPiece(NodePoint p, NodePoint a, NodePoint b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared, 0, 1);
        return p.DistanceTo(new NodePoint(a.X + (t * dx), a.Y + (t * dy)));
    }

    [Test]
    public void A_route_along_the_ways_becomes_a_Loop_of_those_ways_in_order()
    {
        var grid = GridNetwork.Create(junctionsAcross: 6);
        var route = Route((1, 1), (4, 1), (4, 3), (1, 3), (1, 1));
        var match = RouteMatcher.Match(grid, route, Corridor(grid, route));

        var corners = new List<NodePoint> { grid.Node(match.Loop.Start) };
        var at = match.Loop.Start;
        foreach (var traversal in match.Loop.Traversals)
        {
            var segment = grid.Segments[traversal.Segment];
            at = traversal.Forward ? segment.To : segment.From;
            corners.Add(grid.Node(at));
        }
        Assert.That((match.LengthM, match.Unjoined.Count, corners[0], corners[^1], corners.Distinct().Count()),
            Is.EqualTo((1000.0, 0, new NodePoint(100, 100), new NodePoint(100, 100), 10)));
    }

    //  A point-to-point route (T083): matched from its first point to its last, not closed back
    //  to its start; its last junction kept even nearer the one before than the waypoints' spacing.
    [Test]
    public void A_route_that_isn_t_closed_ends_where_it_ends()
    {
        var grid = GridNetwork.Create(junctionsAcross: 6);
        var route = Route((1, 1), (4, 1), (4, 3));
        var match = RouteMatcher.Match(grid, route, Corridor(grid, route), closed: false);

        var last = grid.Segments[match.Loop.Traversals[^1].Segment];
        Assert.That((match.LengthM, grid.Node(match.Loop.Start), grid.Node(match.Loop.Traversals[^1].Forward ? last.To : last.From), match.Unjoined.Count),
            Is.EqualTo((500.0, new NodePoint(100, 100), new NodePoint(400, 300), 0)));
    }

    //  Up a 100 m side street and back: shorter than the waypoints' spacing, so joining them alone
    //  would skip it (found matching a Loop of Wend Your Way's own back on Richmond Park, 2026-10-19).
    [Test]
    public void An_out_and_back_shorter_than_the_waypoints_spacing_is_kept()
    {
        var grid = GridNetwork.Create(junctionsAcross: 6);
        var route = Route((1, 1), (2, 1), (2, 2), (2, 1), (4, 1), (4, 3), (1, 3), (1, 1));
        Assert.That(RouteMatcher.Match(grid, route, Corridor(grid, route)).LengthM, Is.EqualTo(1200));
    }

    //  A dirt path 8 m beside the road the route took, a little shorter than the road (whose
    //  blocks wind to 105 m): matching the shorter one would turn the route's road into Off-road,
    //  and its score with it.
    [Test]
    public void Of_a_road_and_the_path_beside_it_the_one_the_route_took_is_matched()
    {
        var beside = new StoredSegment(new SegmentKey(9001, 8, 11), new NodePoint(100, 100), new NodePoint(400, 100), 301, GroundKind.OffRoad, AccessKind.Allowed);
        var grid = GridNetwork.Create(junctionsAcross: 6, winding: 1.05, extra: [beside]);
        var route = Route((1, 1), (4, 1), (4, 3), (1, 3), (1, 1));
        var corridor = Corridor(grid, route);
        corridor[beside.Key] = 8;

        var match = RouteMatcher.Match(grid, route, corridor);
        Assert.That(match.Loop.Traversals.Select(t => grid.Segments[t.Segment].Key.OsmWayId), Does.Not.Contain(9001L));
    }

    //  Where the Network no longer has a way the route took, the gap is reported, not bridged,
    //  even when the corridor goes the long way round (here, the rest of the Loop backwards).
    [Test]
    public void A_stretch_it_can_t_join_inside_the_corridor_is_reported()
    {
        //  The route's way from (4,1) to (4,2) is gone; every other way round is far longer.
        var grid = Without(GridNetwork.Create(junctionsAcross: 6), (new NodePoint(400, 100), new NodePoint(400, 200)));
        var route = Route((1, 1), (4, 1), (4, 3), (1, 3), (1, 1));

        Assert.That(RouteMatcher.Match(grid, route, Corridor(grid, route)).Unjoined, Has.Count.EqualTo(1));
    }

    //  T089: a 100 m way gone between waypoints 300 m apart: the gap is that way's 100 m, not the
    //  waypoints' 300; the rest of the stretch is matched junction by junction.
    [Test]
    public void A_gap_is_only_as_long_as_the_break()
    {
        var grid = Without(GridNetwork.Create(junctionsAcross: 8), (new NodePoint(500, 100), new NodePoint(600, 100)));
        var route = Route((1, 1), (7, 1), (7, 3), (1, 3), (1, 1));

        var match = RouteMatcher.Match(grid, route, Corridor(grid, route));
        Assert.That((match.LengthM, match.Unjoined.Select(u => Math.Round(u.RouteM)).ToArray()), Is.EqualTo((1500.0, new[] { 100.0 })));
    }

    //  Two ways gone in a row, a junction on the route between them (a spur's, 8 m long, keeps
    //  it in the corridor): one gap, not two.
    [Test]
    public void Gaps_next_to_each_other_are_one()
    {
        var spur = new StoredSegment(new SegmentKey(9001, 14, 9999), new NodePoint(500, 100), new NodePoint(500, 108), 8, GroundKind.TrafficFree, AccessKind.Allowed);
        var grid = Without(GridNetwork.Create(junctionsAcross: 8, extra: [spur]),
            (new NodePoint(400, 100), new NodePoint(500, 100)), (new NodePoint(500, 100), new NodePoint(600, 100)));
        var route = Route((1, 1), (7, 1), (7, 3), (1, 3), (1, 1));

        var match = RouteMatcher.Match(grid, route, Corridor(grid, route));
        Assert.That((match.LengthM, match.Unjoined.Select(u => Math.Round(u.RouteM)).ToArray()), Is.EqualTo((1400.0, new[] { 200.0 })));
    }

    //  T098: a way the route took that lies just outside the corridor (GPS wandering, or a path
    //  drawn a little off) still joins it: of 108 stretches the matcher couldn't join in 60 of the
    //  user's Outings, 86 had a short way just outside. Here the route's way from (5,1) to (6,1)
    //  is on the Network but out of the corridor.
    [Test]
    public void A_way_just_outside_the_corridor_still_joins_the_route()
    {
        var grid = GridNetwork.Create(junctionsAcross: 8);
        var route = Route((1, 1), (7, 1), (7, 3), (1, 3), (1, 1));
        var corridor = Corridor(grid, route);
        foreach (var key in grid.Segments.Where(s => (grid.Node(s.From), grid.Node(s.To)) is var e && (e == (new NodePoint(500, 100), new NodePoint(600, 100)) || e == (new NodePoint(600, 100), new NodePoint(500, 100)))).Select(s => s.Key))
            corridor.Remove(key);

        var match = RouteMatcher.Match(grid, route, corridor);
        Assert.That((match.LengthM, match.Unjoined.Count), Is.EqualTo((1600.0, 0)));
    }


    //  Where a closed route starts, GPS settling: the junction nearest its first point is on a
    //  stub that joins nothing (8 m from it, nearer than the grid's corner). The route is begun at
    //  the next waypoint instead, and closed onto it: no gap where it starts (2026-11-10: of 271 of
    //  the user's Outings made routes, most gaps at their ends were there).
    [Test]
    public void A_closed_route_whose_start_can_t_be_joined_begins_at_the_next_waypoint()
    {
        var stub = new StoredSegment(new SegmentKey(9002, 7001, 7002), new NodePoint(100, 96), new NodePoint(92, 96), 8, GroundKind.OffRoad, AccessKind.Allowed);
        var grid = GridNetwork.Create(junctionsAcross: 6, extra: [stub]);
        NodePoint[] route = [new NodePoint(99, 97), .. Route((1, 1), (4, 1), (4, 3), (1, 3), (1, 1))];

        var match = RouteMatcher.Match(grid, route, Corridor(grid, route));
        Assert.That((match.Unjoined.Count, match.LengthM), Is.EqualTo((0, 1000.0)));
    }
}
