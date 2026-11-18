using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Data.Planning;

/// <summary>A turn point on the map: where, which way, and in words.</summary>
public sealed record RouteTurn(LatLon At, double AlongM, TurnKind Kind, string Words);

/// <summary>A route's turn points from its Segments (T100).</summary>
public interface IRouteTurns
{
    Task<IReadOnlyList<RouteTurn>> OfAsync(IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, CancellationToken cancellationToken = default);
}

/// <summary>
/// A route's turn points from its Segments (PLAN T100, STORY-043): its line in travel order and the
/// junctions on it read from the Network, how many ways meet at each, and the way each goes onto,
/// by its name or its kind; then found as <see cref="TurnPoints"/> finds them.
/// </summary>
public sealed class RouteTurns(NpgsqlDataSource dataSource) : IRouteTurns
{
    /// <summary>What a route goes onto: a way's name, or what kind of way it is.</summary>
    public static string Onto(string? highway, string? name) =>
        !string.IsNullOrWhiteSpace(name) ? name.Trim() : highway switch
        {
            "footway" => "the footpath",
            "path" => "the path",
            "bridleway" => "the bridleway",
            "cycleway" => "the cycle path",
            "track" => "the track",
            "steps" => "the steps",
            "pedestrian" => "the pedestrian street",
            _ => "the road",
        };

    public async Task<IReadOnlyList<RouteTurn>> OfAsync(IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(traversals);
        if (traversals.Count < 2) return [];
        await using var query = dataSource.CreateCommand("""
            with t as (
              select ord, way, f, tt, fwd from unnest(@ways, @froms, @tos, @fwds) with ordinality as x(way, f, tt, fwd, ord))
            select t.ord,
                   array(select ST_X(d.geom) from ST_DumpPoints(case when t.fwd then s.geom else ST_Reverse(s.geom) end) d order by d.path),
                   array(select ST_Y(d.geom) from ST_DumpPoints(case when t.fwd then s.geom else ST_Reverse(s.geom) end) d order by d.path),
                   (select count(*) from network.segments s2
                     where s2.from_node_id = case when t.fwd then t.tt else t.f end or s2.to_node_id = case when t.fwd then t.tt else t.f end),
                   w.tags->>'highway', w.tags->>'name', s.gap is not null, s.length_m
              from t
              join network.segments s on s.osm_way_id = t.way and s.from_node_id = t.f and s.to_node_id = t.tt
              left join osm.ways w on w.way_id = t.way
             order by t.ord
            """);
        query.Parameters.Add(new NpgsqlParameter("ways", traversals.Select(t => t.Key.OsmWayId).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("froms", traversals.Select(t => t.Key.FromNodeId).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("tos", traversals.Select(t => t.Key.ToNodeId).ToArray()));
        query.Parameters.Add(new NpgsqlParameter("fwds", traversals.Select(t => t.Forward).ToArray()));

        // The line, each Segment's points after the first joining on; where each ends, and onto what.
        var line = new List<NodePoint>();
        var ends = new List<(int Point, int Ways)>();
        var onto = new List<string>();
        // Where each Map gap starts on the line (T146b), and how long it is.
        var gaps = new List<(int Point, double LengthM)>();
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetBoolean(6)) gaps.Add((line.Count == 0 ? 0 : line.Count - 1, reader.GetDouble(7)));
                var (xs, ys) = (reader.GetFieldValue<double[]>(1), reader.GetFieldValue<double[]>(2));
                for (var i = line.Count == 0 ? 0 : 1; i < xs.Length; i++) line.Add(new NodePoint(xs[i], ys[i]));
                ends.Add((line.Count - 1, (int)reader.GetInt64(3)));
                onto.Add(Onto(reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }
        // A junction goes onto the next Segment's way.
        var junctions = ends.Take(ends.Count - 1).Select((end, i) => new RouteJunction(end.Point, end.Ways, onto[i + 1])).ToArray();
        var along = new double[line.Count];
        for (var i = 1; i < line.Count; i++) along[i] = along[i - 1] + line[i].DistanceTo(line[i - 1]);
        var turns = TurnPoints.Find(line, junctions)
            .Concat(gaps.Select(g => new TurnPoint(line[g.Point], along[g.Point], TurnKind.MapGap,
                $"A gap in the map ({Math.Max(1, Math.Round(g.LengthM)).ToString(System.Globalization.CultureInfo.InvariantCulture)} m): check it's passable")))
            .OrderBy(t => t.AlongM)
            .ToList();
        if (turns.Count == 0) return [];
        var at = await new MapGeometry(dataSource).ToLatLonAsync([.. turns.Select(t => t.At)], cancellationToken);
        return [.. turns.Select((t, i) => new RouteTurn(at[i], t.AlongM, t.Kind, t.Words))];
    }
}
