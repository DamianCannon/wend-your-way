using System.Buffers.Binary;
using WendYourWay.Core.Network;
using Npgsql;
using NpgsqlTypes;

namespace WendYourWay.Data.Building;

/// <summary>The build's climbing phase (PLAN T049): the ground's height sampled along each Segment.</summary>
internal static class ClimbingPhase
{
    // Segments read at once: a chunk's ids, its geometry and its climbing in memory together.
    private const int ChunkSize = 500_000;

    /// <summary>
    /// Gives each Segment in network_next its ascent and descent from <paramref name="terrain"/>,
    /// within the build's transaction. A Segment of a bridge or tunnel is level between its ends.
    /// </summary>
    internal static async Task<ClimbingBuild> Build(NpgsqlConnection connection, NpgsqlTransaction transaction, TerrainGrid terrain, CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand(
            "create temp table tmp_climbing (id integer primary key, ascent_m real not null, descent_m real not null) on commit drop", connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        int first, last;
        await using (var command = new NpgsqlCommand("select coalesce(min(id), 0), coalesce(max(id), -1) from network_next.segments", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            (first, last) = (reader.GetInt32(0), reader.GetInt32(1));
        }

        long segments = 0;
        var ascentM = 0.0;
        for (var from = first; from <= last; from += ChunkSize)
        {
            var climbs = new List<(int Id, float Ascent, float Descent)>();
            // Square by square, so the terrain's tiles are read once each, not once per Segment.
            await using (var command = new NpgsqlCommand("""
                select s.id, ST_AsBinary(s.geom),
                       coalesce(w.tags->>'bridge', 'no') <> 'no' or coalesce(w.tags->>'tunnel', 'no') <> 'no'
                  from network_next.segments s
                  left join osm.ways w on w.way_id = s.osm_way_id
                 where s.id between @from and @to
                 order by floor(ST_X(ST_StartPoint(s.geom)) / 10000), floor(ST_Y(ST_StartPoint(s.geom)) / 10000)
                """, connection, transaction) { CommandTimeout = 0 })
            {
                command.Parameters.Add(new NpgsqlParameter("from", from));
                command.Parameters.Add(new NpgsqlParameter("to", from + ChunkSize - 1));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var (ascent, descent) = Climbing.Along(Line((byte[])reader[1]), reader.GetBoolean(2), terrain.Height);
                    climbs.Add((reader.GetInt32(0), (float)ascent, (float)descent));
                    ascentM += ascent;
                }
            }

            await using (var writer = await connection.BeginBinaryImportAsync("copy tmp_climbing (id, ascent_m, descent_m) from stdin (format binary)", cancellationToken))
            {
                foreach (var (id, ascent, descent) in climbs)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(ascent, NpgsqlDbType.Real, cancellationToken);
                    await writer.WriteAsync(descent, NpgsqlDbType.Real, cancellationToken);
                }
                await writer.CompleteAsync(cancellationToken);
            }
            segments += climbs.Count;
        }

        await using (var command = new NpgsqlCommand("""
            update network_next.segments s set ascent_m = c.ascent_m, descent_m = c.descent_m
              from tmp_climbing c where c.id = s.id
            """, connection, transaction) { CommandTimeout = 0 })
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return new ClimbingBuild(segments, ascentM);
    }

    // A linestring's points from its well-known binary.
    private static NodePoint[] Line(byte[] wkb)
    {
        var little = wkb[0] == 1;
        int Int(int at) => little ? BinaryPrimitives.ReadInt32LittleEndian(wkb.AsSpan(at)) : BinaryPrimitives.ReadInt32BigEndian(wkb.AsSpan(at));
        double Double(int at) => little ? BinaryPrimitives.ReadDoubleLittleEndian(wkb.AsSpan(at)) : BinaryPrimitives.ReadDoubleBigEndian(wkb.AsSpan(at));
        if (Int(1) != 2) throw new InvalidDataException($"A Segment's geometry is of WKB type {Int(1)}, not a linestring.");
        var points = new NodePoint[Int(5)];
        for (var i = 0; i < points.Length; i++) points[i] = new NodePoint(Double(9 + (16 * i)), Double(17 + (16 * i)));
        return points;
    }
}
