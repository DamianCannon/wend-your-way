using System.Text;
using System.Text.Json;
using Npgsql;

namespace WendYourWay.Data;

/// <summary>
/// The Off-road networks as map tiles (PLAN T022b), for the part of the map in view: zoomed in,
/// every green Segment with its network's size band; zoomed out, each network of 10 km or more as
/// a dot. Coordinates are a 256-pixel tile's, y downwards, as a canvas draws them.
/// </summary>
public sealed class OffRoadTiles(NpgsqlDataSource dataSource)
{
    /// <summary>The farthest zoom drawing dots, not paths: from 13 a tile's paths are few enough to draw.</summary>
    public const int LastDotsZoom = 12;

    /// <summary>The green ground, in metres, at which each band starts: band 0 below the first.</summary>
    public static IReadOnlyList<double> BandsFromM { get; } = [10_000, 30_000, 100_000, 300_000];

    /// <summary>
    /// A tile as JSON: {"lines": [[band, geometry], …]} zoomed in, {"dots": [[x, y, km, band], …]}
    /// zoomed out, a geometry being GeoJSON's in tile pixels.
    /// </summary>
    public async Task<string> JsonAsync(int z, int x, int y, CancellationToken cancellationToken = default)
    {
        if (z is < 0 or > 22 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z) throw new ArgumentOutOfRangeException(nameof(z), "No such tile.");
        return z > LastDotsZoom ? await LinesAsync(z, x, y, cancellationToken) : await DotsAsync(z, x, y, cancellationToken);
    }

    private async Task<string> LinesAsync(int z, int x, int y, CancellationToken cancellationToken)
    {
        await using var query = dataSource.CreateCommand("""
            with tile as (select ST_TileEnvelope(@z, @x, @y) as box)
            select n.metres, ST_AsGeoJSON(ST_AsMVTGeom(ST_Transform(s.geom, 3857), tile.box, 256, 8, true), 1)
              from tile, network.segments s
              join network.segment_offroad o on o.segment_id = s.id
              join network.offroad_networks n on n.id = o.network_id
             where s.geom && ST_Transform(tile.box, 27700)
            """);
        Parameters(query, z, x, y);
        var json = new StringBuilder("{\"lines\":[");
        var first = true;
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(1)) continue;
            json.Append(first ? "" : ",").Append('[').Append(Band(reader.GetFloat(0))).Append(',').Append(reader.GetString(1)).Append(']');
            first = false;
        }
        return json.Append("]}").ToString();
    }

    private async Task<string> DotsAsync(int z, int x, int y, CancellationToken cancellationToken)
    {
        await using var query = dataSource.CreateCommand("""
            with tile as (select ST_TileEnvelope(@z, @x, @y) as box)
            select n.metres, ST_X(p.geom), ST_Y(p.geom)
              from tile, network.offroad_networks n
             cross join lateral (select ST_AsMVTGeom(ST_Transform(ST_Centroid(n.extent), 3857), tile.box, 256, 0, true) as geom) p
             where n.metres >= @smallest and n.extent && ST_Transform(tile.box, 27700) and p.geom is not null
             order by n.metres
            """);
        Parameters(query, z, x, y);
        query.Parameters.Add(new NpgsqlParameter("smallest", BandsFromM[0]));
        var dots = new List<double[]>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var metres = reader.GetFloat(0);
            dots.Add([Math.Round(reader.GetDouble(1), 1), Math.Round(reader.GetDouble(2), 1), Math.Round(metres / 1000), Band(metres)]);
        }
        return JsonSerializer.Serialize(new { dots });
    }

    private static int Band(double metres) => BandsFromM.Count(from => metres >= from);

    private static void Parameters(NpgsqlCommand query, int z, int x, int y)
    {
        query.Parameters.Add(new NpgsqlParameter("z", z));
        query.Parameters.Add(new NpgsqlParameter("x", x));
        query.Parameters.Add(new NpgsqlParameter("y", y));
    }
}
