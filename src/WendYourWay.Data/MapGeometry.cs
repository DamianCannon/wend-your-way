using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Data;

/// <summary>A position in WGS84, as the map and GPX use it.</summary>
public readonly record struct LatLon(double Latitude, double Longitude)
{
    /// <summary>
    /// Metres to <paramref name="other"/>, near enough over a few km: a flat Earth at the latitude
    /// between them (T121: where three copies were).
    /// </summary>
    public double MetresTo(LatLon other)
    {
        const double MetresPerDegree = 111_320;
        var north = (other.Latitude - Latitude) * MetresPerDegree;
        var east = (other.Longitude - Longitude) * MetresPerDegree * Math.Cos((Latitude + other.Latitude) / 2 * Math.PI / 180);
        return Math.Sqrt((north * north) + (east * east));
    }
}

/// <summary>
/// Converts between British National Grid, where the Network lives, and WGS84 at the edges of
/// the app (Leaflet, GPX), using PostGIS so both directions match how the Network was built.
/// </summary>
public sealed class MapGeometry(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// The line of the given Segments travelled in order, each reversed when travelled
    /// backwards, with the junction two consecutive Segments share given once.
    /// </summary>
    /// <exception cref="InvalidOperationException">A Segment isn't in the Network.</exception>
    public async Task<IReadOnlyList<LatLon>> TraceAsync(
        IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, CancellationToken cancellationToken = default) =>
        (await TraceWithWaysAsync(traversals, cancellationToken)).Line;

    /// <summary>
    /// The same line, with the OSM way each point was traced from (T077): for the piece of the line
    /// ending there, so the way under any piece is known on the map.
    /// </summary>
    public async Task<(IReadOnlyList<LatLon> Line, IReadOnlyList<long> Ways)> TraceWithWaysAsync(
        IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, CancellationToken cancellationToken = default)
    {
        var (line, ways, _) = await TraceAlongAsync(traversals, cancellationToken);
        return (line, ways);
    }

    /// <summary>
    /// The same line and ways, with the index of the Traversal each point was traced from (T157): for
    /// putting a Loop's notes on it.
    /// </summary>
    public async Task<(IReadOnlyList<LatLon> Line, IReadOnlyList<long> Ways, IReadOnlyList<int> Of)> TraceAlongAsync(
        IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, CancellationToken cancellationToken = default)
    {
        var traced = await TraceAsync(traversals, 4326, cancellationToken);
        return ([.. traced.Select(p => new LatLon(p.At.Y, p.At.X))], [.. traced.Select(p => traversals[p.Of].Key.OsmWayId)], [.. traced.Select(p => p.Of)]);
    }

    /// <summary>The same line as <see cref="TraceAsync(IReadOnlyList{ValueTuple{SegmentKey, bool}}, CancellationToken)"/>, in British National Grid.</summary>
    /// <exception cref="InvalidOperationException">A Segment isn't in the Network.</exception>
    public async Task<IReadOnlyList<NodePoint>> TraceOnGridAsync(
        IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, CancellationToken cancellationToken = default) =>
        [.. (await TraceAsync(traversals, 27700, cancellationToken)).Select(p => p.At)];

    // Each point of the line, and the Traversal it was traced from.
    private async Task<IReadOnlyList<(NodePoint At, int Of)>> TraceAsync(
        IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, int srid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(traversals);
        await using var query = dataSource.CreateCommand("""
            select ST_X(d.geom), ST_Y(d.geom), t.ord
              from unnest(@ways, @froms, @tos, @forwards) with ordinality as t(way, from_node, to_node, forward, ord)
              join network.segments s
                on s.osm_way_id = t.way and s.from_node_id = t.from_node and s.to_node_id = t.to_node
             cross join lateral ST_DumpPoints(
                   ST_Transform(case when t.forward then s.geom else ST_Reverse(s.geom) end, @srid)) d
             where t.ord = 1 or d.path[1] > 1
             order by t.ord, d.path[1]
            """);
        query.Parameters.Add(new NpgsqlParameter("ways", traversals.Select(t => t.Key.OsmWayId).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("froms", traversals.Select(t => t.Key.FromNodeId).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("tos", traversals.Select(t => t.Key.ToNodeId).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("forwards", traversals.Select(t => t.Forward).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("srid", srid));

        var line = new List<(NodePoint, int)>();
        var traced = new HashSet<long>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            line.Add((new NodePoint(reader.GetDouble(0), reader.GetDouble(1)), (int)reader.GetInt64(2) - 1));
            traced.Add(reader.GetInt64(2));
        }

        // A Segment the Network doesn't have (it was rebuilt mid-Request, say) would leave a gap
        // in the line and the GPX.
        if (traced.Count != traversals.Count)
            throw new InvalidOperationException(
                $"{traversals.Count - traced.Count} of the Loop's {traversals.Count} Segments aren't in the Network any more. Ask again.");
        return line;
    }

    /// <summary>
    /// The Segments nearest a point (British National Grid), nearest first, within
    /// <paramref name="withinM"/> and at most <paramref name="most"/> (T126): each with how far it is,
    /// and how far along it from its start the nearest point of it lies.
    /// </summary>
    public async Task<IReadOnlyList<SegmentNear>> SegmentsNearAsync(NodePoint point, double withinM, int most, CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand("""
            select s.osm_way_id, s.from_node_id, s.to_node_id, ST_Distance(s.geom, q.p), ST_LineLocatePoint(s.geom, q.p) * s.length_m, s.length_m
              from network.segments s, (select ST_SetSRID(ST_MakePoint(@x, @y), 27700) p) q
             where ST_DWithin(s.geom, q.p, @within)
             order by s.geom <-> q.p
             limit @most
            """);
        query.Parameters.Add(new NpgsqlParameter("x", point.X));
        query.Parameters.Add(new NpgsqlParameter("y", point.Y));
        query.Parameters.Add(new NpgsqlParameter("within", withinM));
        query.Parameters.Add(new NpgsqlParameter("most", most));
        var near = new List<SegmentNear>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            near.Add(new SegmentNear(new SegmentKey(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5)));
        return near;
    }

    /// <summary>A position on the map in British National Grid.</summary>
    public async Task<NodePoint> ToGridAsync(LatLon point, CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand(
            "select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326), 27700) p) t");
        query.Parameters.Add(new NpgsqlParameter("lon", point.Longitude));
        query.Parameters.Add(new NpgsqlParameter("lat", point.Latitude));
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new NodePoint(reader.GetDouble(0), reader.GetDouble(1));
    }

    /// <summary>Positions on the map in British National Grid, in the same order (T085).</summary>
    public async Task<IReadOnlyList<NodePoint>> ToGridAsync(IReadOnlyList<LatLon> points, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        await using var query = dataSource.CreateCommand("""
            select ST_X(p), ST_Y(p)
              from (select ST_Transform(ST_SetSRID(ST_MakePoint(lon, lat), 4326), 27700) p, ord
                      from unnest(@lons, @lats) with ordinality as t(lon, lat, ord)) t
             order by ord
            """);
        query.Parameters.Add(new NpgsqlParameter("lons", points.Select(p => p.Longitude).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("lats", points.Select(p => p.Latitude).ToArray()));
        var converted = new List<NodePoint>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) converted.Add(new NodePoint(reader.GetDouble(0), reader.GetDouble(1)));
        return converted;
    }

    /// <summary>
    /// Whether a point (British National Grid) is in England or Wales. Always true when the
    /// Network isn't clipped to them (built from a regional extract): nothing there to test against.
    /// </summary>
    public async Task<bool> InEnglandAndWalesAsync(NodePoint point, CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand("select in_england_and_wales(ST_SetSRID(ST_MakePoint(@x, @y), 27700))");
        query.Parameters.Add(new NpgsqlParameter("x", point.X));
        query.Parameters.Add(new NpgsqlParameter("y", point.Y));
        return (bool)(await query.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Points in British National Grid as positions on the map, in the same order.</summary>
    public async Task<IReadOnlyList<LatLon>> ToLatLonAsync(IReadOnlyList<NodePoint> points, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        await using var query = dataSource.CreateCommand("""
            select ST_Y(p), ST_X(p)
              from (select ST_Transform(ST_SetSRID(ST_MakePoint(x, y), 27700), 4326) p, ord
                      from unnest(@xs, @ys) with ordinality as t(x, y, ord)) t
             order by ord
            """);
        query.Parameters.Add(new NpgsqlParameter("xs", points.Select(p => p.X).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("ys", points.Select(p => p.Y).ToArray()));
        var converted = new List<LatLon>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) converted.Add(new LatLon(reader.GetDouble(0), reader.GetDouble(1)));
        return converted;
    }
}

/// <summary>A Segment near a point (T126): how far away, how far along it, and how long it is, in metres.</summary>
public sealed record SegmentNear(SegmentKey Key, double DistanceM, double AlongM, double LengthM);
