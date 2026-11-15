using Npgsql;

namespace WendYourWay.Data.Planning;

/// <summary>What an amenity on the map is (PLAN T067).</summary>
public enum AmenityKind
{
    Cafe,
    Toilets,
}

/// <summary>A café or public toilets on the map (PLAN T067): its name, if it has one, and where it is.</summary>
public sealed record Amenity(AmenityKind Kind, string? Name, LatLon At);

/// <summary>Finds the cafés and toilets in view (PLAN T067), for the overlay that shows them.</summary>
public interface IAmenities
{
    /// <summary>The cafés and toilets mapped as points in <paramref name="view"/>, at most <see cref="PostgisAmenities.MostInView"/>.</summary>
    Task<IReadOnlyList<Amenity>> InViewAsync(MapBounds view, CancellationToken cancellationToken = default);
}

/// <summary>
/// The cafés and toilets in osm.points: a café drawn only as a building isn't there, as the
/// osm2pgsql style keeps none. None where the map data has no points table (a test database).
/// </summary>
public sealed class PostgisAmenities(NpgsqlDataSource dataSource) : IAmenities
{
    /// <summary>The most in one view, nearest its middle first: a crowded view of a city holds thousands.</summary>
    public const int MostInView = 500;

    public async Task<IReadOnlyList<Amenity>> InViewAsync(MapBounds view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        await using (var exists = dataSource.CreateCommand("select to_regclass('osm.points') is not null"))
        {
            if (await exists.ExecuteScalarAsync(cancellationToken) is not true) return [];
        }

        var middle = view.Middle;
        await using var query = dataSource.CreateCommand("""
            select p.tags->>'amenity', p.tags->>'name', ST_Y(p.geom), ST_X(p.geom)
              from osm.points p
             where p.geom && ST_MakeEnvelope(@west, @south, @east, @north, 4326)
               and p.tags->>'amenity' in ('cafe', 'toilets')
             order by p.geom <-> ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)
             limit @most
            """);
        query.Parameters.Add(new NpgsqlParameter("west", view.West));
        query.Parameters.Add(new NpgsqlParameter("south", view.South));
        query.Parameters.Add(new NpgsqlParameter("east", view.East));
        query.Parameters.Add(new NpgsqlParameter("north", view.North));
        query.Parameters.Add(new NpgsqlParameter("lon", middle.Longitude));
        query.Parameters.Add(new NpgsqlParameter("lat", middle.Latitude));
        query.Parameters.Add(new NpgsqlParameter("most", MostInView));

        var found = new List<Amenity>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(new Amenity(reader.GetString(0) == "cafe" ? AmenityKind.Cafe : AmenityKind.Toilets,
                reader.IsDBNull(1) ? null : reader.GetString(1), new LatLon(reader.GetDouble(2), reader.GetDouble(3))));
        }
        return found;
    }
}
