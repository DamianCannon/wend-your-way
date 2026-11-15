using WendYourWay.Core.Places;
using Npgsql;

namespace WendYourWay.Data.Places;

/// <summary>A place found by search (PLAN T064): its name, kind, what tells it apart, and where it is.</summary>
public sealed record FoundPlace(string Name, PlaceKind Kind, string? Context, LatLon At)
{
    /// <summary>The zoom the map shows it at.</summary>
    public int Zoom => PlaceSearch.ZoomFor(Kind);
}

/// <summary>Finds places by name and postcode (PLAN T064).</summary>
public interface IPlaceSearch
{
    /// <summary>
    /// The places <paramref name="text"/> names, best first, those nearer <paramref name="middle"/>
    /// (the middle of the map, never home) first among equals; none for under two characters.
    /// </summary>
    Task<IReadOnlyList<FoundPlace>> SearchAsync(string text, LatLon middle, CancellationToken cancellationToken = default);

    /// <summary>The postcode nearest <paramref name="at"/>, if one is within a kilometre: for a start clicked on the map.</summary>
    Task<FoundPlace?> NearestPostcodeAsync(LatLon at, CancellationToken cancellationToken = default);
}

/// <summary>
/// The places loaded by `wend places import`, searched in PostgreSQL: the nearest
/// <see cref="Nearest"/> matches to the middle of the map, ranked by <see cref="PlaceSearch.Rank"/>.
/// What's typed is never logged: a postcode typed may be home's.
/// </summary>
public sealed class PostgisPlaceSearch(NpgsqlDataSource dataSource) : IPlaceSearch
{
    /// <summary>How many matches nearest the middle of the map are ranked.</summary>
    public const int Nearest = 400;

    /// <summary>How far off a postcode can be and still be a place's: out on a moor there's none.</summary>
    public const double NearestPostcodeM = 1000;

    public async Task<FoundPlace?> NearestPostcodeAsync(LatLon at, CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand("""
            with seen as (select ST_Transform(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326), 27700) as at)
            select n.name, n.context, ST_Y(ST_Transform(n.geom, 4326)), ST_X(ST_Transform(n.geom, 4326)), ST_Distance(n.geom, seen.at)
              from places.names n, seen
             where n.kind = 'Postcode'
             order by n.geom <-> seen.at
             limit 1
            """);
        query.Parameters.Add(new NpgsqlParameter("lon", at.Longitude));
        query.Parameters.Add(new NpgsqlParameter("lat", at.Latitude));
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetDouble(4) > NearestPostcodeM) return null;
        return new FoundPlace(reader.GetString(0), PlaceKind.Postcode, reader.IsDBNull(1) ? null : reader.GetString(1), new LatLon(reader.GetDouble(2), reader.GetDouble(3)));
    }

    public async Task<IReadOnlyList<FoundPlace>> SearchAsync(string text, LatLon middle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var postcode = PlaceSearch.PostcodeKey(text);
        var key = postcode ?? PlaceSearch.Key(text);
        if (key.Length < PlaceSearch.FromCharacters) return [];

        // Postcodes by their own index; names by the start of the name or of a later word.
        await using var query = dataSource.CreateCommand(postcode is not null
            ? """
              with seen as (select ST_Transform(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326), 27700) as middle)
              select n.id, n.name, n.kind, n.context, n.key, ST_Y(ST_Transform(n.geom, 4326)), ST_X(ST_Transform(n.geom, 4326)), ST_Distance(n.geom, seen.middle)
                from places.names n, seen
               where n.kind in ('Postcode', 'District') and n.key like @key || '%'
               order by n.key
               limit @nearest
              """
            : """
              with seen as (select ST_Transform(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326), 27700) as middle)
              select n.id, n.name, n.kind, n.context, n.key, ST_Y(ST_Transform(n.geom, 4326)), ST_X(ST_Transform(n.geom, 4326)), ST_Distance(n.geom, seen.middle)
                from places.names n, seen
               where n.kind not in ('Postcode', 'District') and (n.key like @key || '%' or n.key like '% ' || @key || '%')
               order by n.geom <-> seen.middle
               limit @nearest
              """);
        query.Parameters.Add(new NpgsqlParameter("key", key));
        query.Parameters.Add(new NpgsqlParameter("lon", middle.Longitude));
        query.Parameters.Add(new NpgsqlParameter("lat", middle.Latitude));
        query.Parameters.Add(new NpgsqlParameter("nearest", Nearest));

        var candidates = new List<PlaceCandidate>();
        var found = new Dictionary<int, FoundPlace>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(0);
            var kind = Enum.Parse<PlaceKind>(reader.GetString(2));
            candidates.Add(new PlaceCandidate(id, reader.GetString(4), kind, reader.GetDouble(7)));
            found[id] = new FoundPlace(reader.GetString(1), kind, reader.IsDBNull(3) ? null : reader.GetString(3), new LatLon(reader.GetDouble(5), reader.GetDouble(6)));
        }
        return [.. PlaceSearch.Rank(candidates, key).Select(c => found[c.Id])];
    }
}
