using System.Globalization;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Data.Planning;

/// <summary>A route matched onto the Network, with what's needed to judge the match.</summary>
/// <param name="Slice">The Network around the route, which the matched Loop indexes into.</param>
/// <param name="Match">The route as a Loop of Segments, and anything that couldn't be joined.</param>
/// <param name="RouteLengthM">The route's own length.</param>
/// <param name="ShareNearMatch">The share of the route within 10 m of the matched Segments.</param>
public sealed record MatchedRoute(NetworkSlice Slice, RouteMatch Match, double RouteLengthM, double ShareNearMatch)
{
    /// <summary>The route's points on the grid, one for each point given, in order.</summary>
    public IReadOnlyList<NodePoint> Points { get; init; } = [];
}

/// <summary>
/// Matches a route planned elsewhere (a Trail Router GPX) onto the Network, so the scorer can judge
/// it like a Loop of Wend Your Way's own (increment 1). It only reads: the specs that use it run on a
/// read-only connection to the real Network. The slice is centred on the route itself, so nothing
/// about where the user lives is needed.
/// </summary>
public sealed class RouteMatching(NpgsqlDataSource dataSource)
{
    // Segments within this of the route are its corridor: a route's drawing and OSM's can differ
    // by a few metres.
    // T098: 40 m, not 20: a route's GPS wanders further than OSM's ways are off (on 200 of the
    // user's Outings, with Segments outside the corridor allowed, 202 gaps against 219 at 30 m).
    private const double CorridorM = 40;

    /// <summary>Why a route too spread out to read at once is refused (T086).</summary>
    public const string TooSpread = "That route spreads too far across the map to read at once: more than about 90 km from one side to the other.";

    // A route sample this close to a matched Segment counts as matched.
    private const double NearM = 10;

    // The route in British National Grid, and its straight pieces, from @lons and @lats.
    private const string Route = """
        route as (
          select ST_Transform(ST_SetSRID(ST_MakeLine(array(
                     select ST_MakePoint(lon, lat) from unnest(@lons, @lats) with ordinality as p(lon, lat, ord) order by ord)), 4326), 27700) as geom),
        pieces as (
          select d.geom from route r cross join lateral ST_DumpSegments(r.geom) d where ST_Length(d.geom) > 0)
        """;

    /// <param name="route">The route's points in order, back to its start unless not <paramref name="closed"/>.</param>
    /// <param name="activity">Whose slice it's matched on (T083): what a Nudge of it may use.</param>
    /// <param name="marginM">How far beyond the route the slice reaches.</param>
    /// <param name="closed">Back to its start, or a point-to-point route (T083).</param>
    /// <param name="maxRadiusM">The furthest its slice may reach from its middle (T086), or no limit.</param>
    /// <exception cref="RouteNotMatchedException">Its slice would reach further than <paramref name="maxRadiusM"/>.</exception>
    public async Task<MatchedRoute> MatchAsync(
        IReadOnlyList<LatLon> route, Activity activity = Activity.Run, double marginM = 200, bool closed = true, double? maxRadiusM = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var lons = route.Select(p => p.Longitude).ToArray();
        var lats = route.Select(p => p.Latitude).ToArray();
        NpgsqlCommand Command(string sql)
        {
            var command = dataSource.CreateCommand(sql);
            command.CommandTimeout = 0;
            command.Parameters.Add(new NpgsqlParameter("lons", lons));
            command.Parameters.Add(new NpgsqlParameter("lats", lats));
            return command;
        }

        // Its points, length and extent.
        var points = new List<NodePoint>();
        double lengthM, centreX, centreY, reachM;
        await using (var query = Command($"""
            with {Route}
            select ST_Length(geom), ST_X(c), ST_Y(c), ST_MaxDistance(c, geom),
                   array(select ST_X(d.geom) from ST_DumpPoints(geom) d order by d.path),
                   array(select ST_Y(d.geom) from ST_DumpPoints(geom) d order by d.path)
              from (select geom, ST_Centroid(ST_Envelope(geom)) as c from route) r
            """))
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            (lengthM, centreX, centreY, reachM) = (reader.GetDouble(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3));
            var xs = reader.GetFieldValue<double[]>(4);
            var ys = reader.GetFieldValue<double[]>(5);
            for (var i = 0; i < xs.Length; i++) points.Add(new NodePoint(xs[i], ys[i]));
        }
        if (reachM + marginM > maxRadiusM) throw new RouteNotMatchedException(TooSpread);

        // The corridor: Segments near the route, found piece by piece (against a whole Loop the
        // spatial index barely helps), each with its mean distance from the route.
        var corridor = new Dictionary<SegmentKey, double>();
        await using (var query = Command(string.Create(CultureInfo.InvariantCulture, $"""
            with {Route},
            near as (
              select distinct s.id, s.osm_way_id, s.from_node_id, s.to_node_id, s.geom
                from pieces p
                join network.segments s on ST_DWithin(s.geom, p.geom, {CorridorM}))
            select n.osm_way_id, n.from_node_id, n.to_node_id,
                   sum(x.weight * ST_Distance(x.point, r.geom)) / sum(x.weight)
              from near n
             cross join route r
             cross join lateral sample_line(n.geom, 5) x
             group by n.id, n.osm_way_id, n.from_node_id, n.to_node_id
            """)))
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                corridor[new SegmentKey(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2))] = reader.GetDouble(3);
        }

        var slice = await new PostgisNetworkSlices(dataSource).AroundAsync(new NodePoint(centreX, centreY), reachM + marginM, activity, cancellationToken: cancellationToken);
        // CPU-bound for a long route: off the caller's thread, as a Request's generation is.
        var match = await Task.Run(() => RouteMatcher.Match(slice, points, corridor, closed), cancellationToken);

        // How much of the route runs along what was matched, sampled every 5 m.
        var keys = match.Loop.Traversals.Select(t => slice.Segments[t.Segment].Key).Distinct().ToArray();
        await using var share = Command(string.Create(CultureInfo.InvariantCulture, $"""
            with {Route},
            matched as (
              select ST_Collect(s.geom) as geom
                from unnest(@ways, @froms, @tos) as k(way, f, t)
                join network.segments s on s.osm_way_id = k.way and s.from_node_id = k.f and s.to_node_id = k.t)
            select coalesce(sum(x.weight) filter (where ST_DWithin(m.geom, x.point, {NearM})), 0) / sum(x.weight)
              from route r cross join matched m cross join lateral sample_line(r.geom, 5) x
            """));
        share.Parameters.Add(new NpgsqlParameter("ways", keys.Select(k => k.OsmWayId).ToArray()));
        share.Parameters.Add(new NpgsqlParameter("froms", keys.Select(k => k.FromNodeId).ToArray()));
        share.Parameters.Add(new NpgsqlParameter("tos", keys.Select(k => k.ToNodeId).ToArray()));
        var shareNear = Convert.ToDouble(await share.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

        return new MatchedRoute(slice, match, lengthM, shareNear) { Points = points };
    }
}
