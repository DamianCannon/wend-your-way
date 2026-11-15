using WendYourWay.Core.Network;
using Npgsql;
using NpgsqlTypes;

namespace WendYourWay.Data.Building;

/// <summary>What finding the Trailheads did (T023).</summary>
/// <param name="CarParks">Car parks the public may use beside a large Off-road network.</param>
/// <param name="Trailheads">Those kept as Trailheads.</param>
public sealed record TrailheadBuild(long CarParks, long Trailheads);

/// <summary>
/// The build's Trailheads phase (PLAN T023), after the Off-road networks: car parks the public may
/// use beside a network of at least <see cref="Trailheads.SmallestNetworkM"/>, thinned by
/// <see cref="Trailheads.Thin"/>.
/// </summary>
public static class TrailheadsPhase
{
    /// <summary>Finds the Trailheads of the Network in <paramref name="schema"/>, in place of any found before.</summary>
    public static async Task<TrailheadBuild> Build(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (schema is not ("network" or "network_next")) throw new ArgumentOutOfRangeException(nameof(schema), schema, "Only network or network_next.");

        var candidates = new List<(TrailheadCandidate Candidate, string Kind)>();
        await using (var command = new NpgsqlCommand($"""
            with parks as (
                select 'area' as kind, a.area_id as osm_id, a.tags, ST_Transform(a.geom, 27700) as geom
                  from osm.areas a where a.tags->>'amenity' = 'parking'
                union all
                select 'point', p.node_id, p.tags, ST_Transform(p.geom, 27700)
                  from osm.points p where p.tags->>'amenity' = 'parking'
            ), open_to_all as (
                select * from parks
                 where coalesce(tags->>'access', '') not in ('private', 'customers', 'permit', 'no', 'residents', 'delivery', 'destination')
                   and coalesce(tags->>'parking', '') not in ('multi-storey', 'underground', 'rooftop', 'garage', 'garages', 'carports', 'sheds', 'lane')
            ), beside_green as (
                -- Beside a large green area, so not a shop's or an office's (2026-10-28).
                select * from open_to_all o
                 where exists (
                     select 1 from osm.areas a
                      where (a.tags->>'leisure' in ('park', 'common', 'nature_reserve') or a.tags->>'landuse' in ('forest', 'meadow')
                             or a.tags->>'natural' in ('wood', 'heath', 'grassland', 'scrub'))
                        and ST_DWithin(a.geom, ST_Transform(o.geom, 4326), 0.005)
                        and ST_DWithin(ST_Transform(a.geom, 27700), o.geom, @green_beside)
                        and ST_Area(ST_Transform(a.geom, 27700)) >= @green_area)
            )
            select o.kind, o.osm_id, ST_X(ST_PointOnSurface(o.geom)), ST_Y(ST_PointOnSurface(o.geom)), o.tags->>'name',
                   ST_Area(o.geom), beside.id, beside.metres
              from beside_green o
              cross join lateral (
                  select n.id, n.metres
                    from {schema}.segments s
                    join {schema}.segment_offroad so on so.segment_id = s.id
                    join {schema}.offroad_networks n on n.id = so.network_id
                   where ST_DWithin(s.geom, o.geom, @beside) and n.metres >= @smallest
                   order by n.metres desc
                   limit 1) beside
            """, connection, transaction) { CommandTimeout = 0 })
        {
            command.Parameters.Add(new NpgsqlParameter("beside", Trailheads.BesideM));
            command.Parameters.Add(new NpgsqlParameter("smallest", Trailheads.SmallestNetworkM));
            command.Parameters.Add(new NpgsqlParameter("green_beside", Trailheads.BesideGreenAreaM));
            command.Parameters.Add(new NpgsqlParameter("green_area", Trailheads.GreenAreaM2));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add((new TrailheadCandidate(reader.GetInt64(1), new NodePoint(reader.GetDouble(2), reader.GetDouble(3)),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDouble(5), reader.GetInt32(6), reader.GetFloat(7)), reader.GetString(0)));
            }
        }

        // An area and a node can share an id: each is known by both.
        var kindOf = candidates.GroupBy(c => c.Candidate).ToDictionary(g => g.Key, g => g.First().Kind);
        var kept = Trailheads.Thin(candidates.Select(c => c.Candidate));

        await using (var command = new NpgsqlCommand($"truncate {schema}.trailheads restart identity", connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var writer = await connection.BeginBinaryImportAsync(
            $"copy {schema}.trailheads (osm_kind, osm_id, name, geom, network_id, network_m) from stdin (format binary)", cancellationToken))
        {
            foreach (var trailhead in kept)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(kindOf[trailhead], NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(trailhead.OsmId, NpgsqlDbType.Bigint, cancellationToken);
                if (trailhead.Name is null) await writer.WriteNullAsync(cancellationToken);
                else await writer.WriteAsync(trailhead.Name, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(Point(trailhead.At), NpgsqlDbType.Bytea, cancellationToken);
                await writer.WriteAsync(trailhead.Network, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync((float)trailhead.NetworkM, NpgsqlDbType.Real, cancellationToken);
            }
            await writer.CompleteAsync(cancellationToken);
        }

        // What each is called, and the roads a car reaches it by, once here rather than on every
        // Find a start (user, 2026-10-29). After the driving phase, whose roads these are.
        await using (var command = new NpgsqlCommand($"""
            update {schema}.trailheads t
               set label = coalesce(t.name, (
                       select a.tags->>'name'
                         from osm.areas a
                        where a.tags ? 'name'
                          and (a.tags->>'leisure' in ('park', 'common', 'nature_reserve')
                               or a.tags->>'landuse' = 'forest' or a.tags->>'natural' in ('wood', 'heath'))
                          and ST_DWithin(a.geom, ST_Transform(t.geom, 4326), 0.01)
                          and ST_DWithin(ST_Transform(a.geom, 27700), t.geom, 500)
                        order by ST_Distance(ST_Transform(a.geom, 27700), t.geom)
                        limit 1), 'Car park')
            """, connection, transaction) { CommandTimeout = 0 })
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // What the map says of each (T066): its own fee, spaces and hours, and toilets, a café and
        // a pub near; never a guess. The fee and capacity are read by the Core's own rules.
        var tags = new List<(int Id, string? Fee, string? Capacity)>();
        await using (var command = new NpgsqlCommand($"""
            select t.id, coalesce(a.tags, p.tags)->>'fee', coalesce(a.tags, p.tags)->>'capacity'
              from {schema}.trailheads t
              left join osm.areas a on t.osm_kind = 'area' and a.area_id = t.osm_id
              left join osm.points p on t.osm_kind = 'point' and p.node_id = t.osm_id
            """, connection, transaction) { CommandTimeout = 0 })
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                tags.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        await using (var command = new NpgsqlCommand($"""
            update {schema}.trailheads t
               set fee = d.fee, capacity = d.capacity
              from unnest(@ids, @fees, @capacities) as d(id, fee, capacity)
             where t.id = d.id
            """, connection, transaction) { CommandTimeout = 0 })
        {
            command.Parameters.Add(new NpgsqlParameter("ids", tags.Select(t => t.Id).ToArray()));
            command.Parameters.Add(new NpgsqlParameter("fees", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = tags.Select(t => CarParkDetails.FeeOf(t.Fee)?.ToString()).ToArray() });
            command.Parameters.Add(new NpgsqlParameter("capacities", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = tags.Select(t => CarParkDetails.CapacityOf(t.Capacity)).ToArray() });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var command = new NpgsqlCommand($"""
            update {schema}.trailheads t
               set hours = nullif(coalesce(a.tags, p.tags)->>'opening_hours', ''),
                   toilets = exists (select 1 from osm.points n where n.tags->>'amenity' = 'toilets' and {Near}),
                   cafe = exists (select 1 from osm.points n where n.tags->>'amenity' = 'cafe' and {Near}),
                   pub = exists (select 1 from osm.points n where n.tags->>'amenity' = 'pub' and {Near})
              from {schema}.trailheads s
              left join osm.areas a on s.osm_kind = 'area' and a.area_id = s.osm_id
              left join osm.points p on s.osm_kind = 'point' and p.node_id = s.osm_id
             where s.id = t.id
            """, connection, transaction) { CommandTimeout = 0 })
        {
            command.Parameters.Add(new NpgsqlParameter("nearby", CarParkDetails.NearbyM));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return new TrailheadBuild(candidates.Count, kept.Count);
    }

    // Within CarParkDetails.NearbyM of a Trailhead t: by the osm index in degrees first (a little
    // over 300 m anywhere in England and Wales), then in metres.
    private const string Near = "ST_DWithin(n.geom, ST_Transform(t.geom, 4326), 0.006) and ST_DWithin(ST_Transform(n.geom, 27700), t.geom, @nearby)";

    // A point in EWKB with British National Grid's SRID, as COPY's binary format takes a geometry.
    private static byte[] Point(NodePoint at)
    {
        var bytes = new byte[25];
        bytes[0] = 1;   // little-endian
        BitConverter.TryWriteBytes(bytes.AsSpan(1), 0x20000001u);   // a point with an SRID
        BitConverter.TryWriteBytes(bytes.AsSpan(5), 27700);
        BitConverter.TryWriteBytes(bytes.AsSpan(9), at.X);
        BitConverter.TryWriteBytes(bytes.AsSpan(17), at.Y);
        return bytes;
    }
}
