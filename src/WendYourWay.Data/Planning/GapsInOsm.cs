using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Data.Planning;

/// <summary>
/// A short gap in OSM (T185): a path's dead end <paramref name="From"/> that stops short of another way at
/// <paramref name="To"/>, with nothing mapped across, as the build found it (T146a); joined under 5 m,
/// possible to 15 m. With the OSM ways either side, to look at in OpenStreetMap.
/// </summary>
public sealed record GapInOsm(MapGapKind Kind, double LengthM, LatLon From, LatLon To, long PathWay, long? OntoWay);

/// <summary>Finds the short gaps in view (T185, #189), for the overlay that shows them.</summary>
public interface IGapsInOsm
{
    /// <summary>The gaps in <paramref name="view"/>, nearest its middle first, at most <see cref="PostgisGapsInOsm.MostInView"/>.</summary>
    Task<IReadOnlyList<GapInOsm>> InViewAsync(MapBounds view, CancellationToken cancellationToken = default);
}

/// <summary>The gaps among the Network's Segments, each with the way it stops short of: the one whose Segment it meets.</summary>
public sealed class PostgisGapsInOsm(NpgsqlDataSource dataSource) : IGapsInOsm
{
    /// <summary>The most in one view: a city's view at the overlay's zoom holds a few hundred.</summary>
    public const int MostInView = 1000;

    public async Task<IReadOnlyList<GapInOsm>> InViewAsync(MapBounds view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        var middle = view.Middle;
        await using var query = dataSource.CreateCommand("""
            with v as (select ST_Transform(ST_MakeEnvelope(@west, @south, @east, @north, 4326), 27700) as box,
                              ST_Transform(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326), 27700) as middle)
            select s.gap, s.length_m, s.osm_way_id,
                   (select o.osm_way_id from network.segments o
                     where s.to_node_id in (o.from_node_id, o.to_node_id) and o.gap is null and o.osm_way_id <> s.osm_way_id
                     limit 1),
                   ST_Y(a), ST_X(a), ST_Y(b), ST_X(b)
              from network.segments s, v
             cross join lateral (select ST_Transform(ST_StartPoint(s.geom), 4326) as a, ST_Transform(ST_EndPoint(s.geom), 4326) as b) e
             where s.gap is not null and s.geom && v.box
             order by s.geom <-> v.middle
             limit @most
            """);
        query.Parameters.Add(new NpgsqlParameter("west", view.West));
        query.Parameters.Add(new NpgsqlParameter("south", view.South));
        query.Parameters.Add(new NpgsqlParameter("east", view.East));
        query.Parameters.Add(new NpgsqlParameter("north", view.North));
        query.Parameters.Add(new NpgsqlParameter("lon", middle.Longitude));
        query.Parameters.Add(new NpgsqlParameter("lat", middle.Latitude));
        query.Parameters.Add(new NpgsqlParameter("most", MostInView));

        var found = new List<GapInOsm>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(new GapInOsm(reader.GetString(0) == "joined" ? MapGapKind.Joined : MapGapKind.Possible, reader.GetDouble(1),
                new LatLon(reader.GetDouble(4), reader.GetDouble(5)), new LatLon(reader.GetDouble(6), reader.GetDouble(7)),
                reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetInt64(3)));
        }
        return found;
    }
}
