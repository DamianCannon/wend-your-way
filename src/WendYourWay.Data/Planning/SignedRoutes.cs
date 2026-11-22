using Npgsql;

namespace WendYourWay.Data.Planning;

/// <summary>A signed walking, cycling, MTB or running route (T176): an OSM route relation, as the map keeps it.</summary>
/// <param name="RelationId">Its OSM relation.</param>
/// <param name="Kind">OSM's <c>route</c>: hiking, foot, walking, running, bicycle or mtb.</param>
/// <param name="Ref">The code signed on it, such as a National Cycle Route's number, if it has one.</param>
/// <param name="Name">Its name, if it has one.</param>
/// <param name="Network">OSM's <c>network</c>, such as nwn or lcn: its level, international to local.</param>
/// <param name="Website">Its own website, if it's tagged with one.</param>
public sealed record SignedRoute(long RelationId, string Kind, string? Ref, string? Name, string? Network, string? Website)
{
    /// <summary>Waymarked Trails' page for it: the whole route, its length, elevation and GPX.</summary>
    public Uri WaymarkedTrails => new($"https://{Kind switch { "bicycle" => "cycling", "mtb" => "mtb", _ => "hiking" }}.waymarkedtrails.org/#route?id={RelationId}");

    /// <summary>Its relation on OpenStreetMap.</summary>
    public Uri OpenStreetMap => new($"https://www.openstreetmap.org/relation/{RelationId}");
}

/// <summary>Finds the signed routes near a point (T176), for the card shown on pointing at one.</summary>
public interface ISignedRoutes
{
    /// <summary>The routes with a way within <paramref name="metres"/> of <paramref name="at"/> (at most <see cref="PostgisSignedRoutes.FurthestM"/>), the widest-reaching first.</summary>
    Task<IReadOnlyList<SignedRoute>> NearAsync(LatLon at, double metres, CancellationToken cancellationToken = default);
}

/// <summary>
/// The routes in osm.routes, whose ways are in osm.route_ways (kept for Corridors, T037). None where
/// the map data has no routes table (a test database).
/// </summary>
public sealed class PostgisSignedRoutes(NpgsqlDataSource dataSource) : ISignedRoutes
{
    /// <summary>The furthest searched: a pointer's reach, never a county's routes.</summary>
    public const double FurthestM = 200;

    /// <summary>The most named at once: a city's busiest path carries a handful.</summary>
    public const int Most = 8;

    public async Task<IReadOnlyList<SignedRoute>> NearAsync(LatLon at, double metres, CancellationToken cancellationToken = default)
    {
        await using (var exists = dataSource.CreateCommand("select to_regclass('osm.routes') is not null and to_regclass('osm.route_ways') is not null"))
        {
            if (await exists.ExecuteScalarAsync(cancellationToken) is not true) return [];
        }

        // International, national, regional, then local routes, as Waymarked Trails ranks them.
        await using var query = dataSource.CreateCommand("""
            with here as (select ST_SetSRID(ST_MakePoint(@lon, @lat), 4326) g),
            near as (
              select w.way_id from osm.ways w, here
               where w.geom && ST_Expand(here.g, @metres / 111000.0 / cos(radians(@lat)))
                 and ST_DWithin(w.geom::geography, here.g::geography, @metres))
            select distinct r.relation_id, r.tags->>'route', r.tags->>'ref', r.tags->>'name', r.tags->>'network', r.tags->>'website',
                   case left(coalesce(r.tags->>'network', ''), 1) when 'i' then 0 when 'n' then 1 when 'r' then 2 when 'l' then 3 else 4 end rank
              from near
              join osm.route_ways rw on rw.way_id = near.way_id
              join osm.routes r on r.relation_id = rw.relation_id
             order by rank, r.tags->>'ref', r.tags->>'name', r.relation_id
             limit @most
            """);
        query.Parameters.Add(new NpgsqlParameter("lon", at.Longitude));
        query.Parameters.Add(new NpgsqlParameter("lat", at.Latitude));
        query.Parameters.Add(new NpgsqlParameter("metres", Math.Clamp(metres, 1, FurthestM)));
        query.Parameters.Add(new NpgsqlParameter("most", Most));

        string? Text(NpgsqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);
        var found = new List<SignedRoute>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(new SignedRoute(reader.GetInt64(0), reader.GetString(1), Text(reader, 2), Text(reader, 3), Text(reader, 4), WebAddress(Text(reader, 5))));
        }
        return found;
    }

    // OSM's word, linked on the card: only an http or https address, never a script.
    private static string? WebAddress(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var address) && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            ? address.AbsoluteUri : null;
}
