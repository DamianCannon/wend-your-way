using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Data.Planning;

/// <summary>A Mark on a Segment (T082): Blocked, for now. Its line, to draw it; none if its Segment is gone.</summary>
public sealed record Mark(int Id, SegmentKey Segment, IReadOnlyList<LatLon> Line);

/// <summary>The user's Marks (T082, SPEC R9.2), kept by Segment identity.</summary>
public interface IMarks
{
    /// <summary>The Segment Blocked: no Loop uses it from now on. Its Mark's id; the same Mark if it was already.</summary>
    Task<int> BlockAsync(SegmentKey segment, CancellationToken cancellationToken = default);

    /// <summary>Every Mark, with its Segment's line where the Network still has it.</summary>
    Task<IReadOnlyList<Mark>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The Mark gone: its Segment as the map says again.</summary>
    Task RemoveAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>Marks in app.marks.</summary>
public sealed class PostgisMarks(NpgsqlDataSource dataSource) : IMarks
{
    public async Task<int> BlockAsync(SegmentKey segment, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            insert into app.marks (osm_way_id, from_node_id, to_node_id, kind) values (@way, @from, @to, 'blocked')
            on conflict (osm_way_id, from_node_id, to_node_id) do update set kind = excluded.kind
            returning id
            """);
        command.Parameters.Add(new NpgsqlParameter("way", segment.OsmWayId));
        command.Parameters.Add(new NpgsqlParameter("from", segment.FromNodeId));
        command.Parameters.Add(new NpgsqlParameter("to", segment.ToNodeId));
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<IReadOnlyList<Mark>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand("""
            select m.id, m.osm_way_id, m.from_node_id, m.to_node_id,
                   (select array_agg(ST_Y(d.geom) order by d.path) || array_agg(ST_X(d.geom) order by d.path)
                      from ST_DumpPoints(ST_Transform(s.geom, 4326)) d)
              from app.marks m
              left join network.segments s
                on s.osm_way_id = m.osm_way_id and s.from_node_id = m.from_node_id and s.to_node_id = m.to_node_id
             order by m.id
            """);
        var marks = new List<Mark>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // Latitudes then longitudes, in one array.
            var both = reader.IsDBNull(4) ? [] : reader.GetFieldValue<double[]>(4);
            var half = both.Length / 2;
            marks.Add(new Mark(reader.GetInt32(0), new SegmentKey(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)),
                [.. Enumerable.Range(0, half).Select(i => new LatLon(both[i], both[half + i]))]));
        }
        return marks;
    }

    public async Task RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("delete from app.marks where id = @id");
        command.Parameters.Add(new NpgsqlParameter("id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
