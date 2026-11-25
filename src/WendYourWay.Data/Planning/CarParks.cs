using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Data.Planning;

/// <summary>A place to start a Loop: a Trailhead around here (PLAN T060), or a postcode searched for (T064).</summary>
/// <param name="Name">The car park's name, else the nearest named park, common, wood or nature reserve, else "Car park".</param>
/// <param name="At">Where it is.</param>
/// <param name="NetworkM">The green ground of the Off-road network it's beside.</param>
/// <param name="Details">What's known of the car park beyond where it is (T066); none for a postcode.</param>
public sealed record FoundStart(string Name, LatLon At, double NetworkM, CarParkDetails? Details = null);

/// <summary>The part of the map in view, in degrees.</summary>
public sealed record MapBounds(double South, double West, double North, double East)
{
    /// <summary>The middle of the view.</summary>
    public LatLon Middle => new((South + North) / 2, (West + East) / 2);
}

/// <summary>The car parks in view (PLAN T060): all of them for the map, and the best for the panel.</summary>
/// <param name="OnMap">Every Trailhead in view, the largest networks first, at most <see cref="PostgisCarParks.MostOnMap"/>.</param>
/// <param name="Best">The best of them to start a Loop of the Distance from, best first.</param>
public sealed record CarParksAround(IReadOnlyList<FoundStart> OnMap, IReadOnlyList<FoundStart> Best);

/// <summary>Finds the car parks around here (PLAN T060): no drive and no home, so at once.</summary>
public interface ICarParks
{
    /// <summary>
    /// The Trailheads in <paramref name="view"/>, and the best for a Loop of about
    /// <paramref name="distanceKm"/>; for a Gravel Ride, only those beside a Gravel Off-road
    /// network, ranked by it (T073).
    /// </summary>
    Task<CarParksAround> AroundAsync(MapBounds view, double distanceKm, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, CancellationToken cancellationToken = default);
}

/// <summary>Reads the stored Trailheads in view by their spatial index and ranks them with <see cref="Trailheads.Around"/>.</summary>
public sealed class PostgisCarParks(NpgsqlDataSource dataSource) : ICarParks
{
    /// <summary>The most car parks marked on the map at once: enough for a county, few enough to draw.</summary>
    public const int MostOnMap = 300;

    public async Task<CarParksAround> AroundAsync(MapBounds view, double distanceKm, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        var middle = view.Middle;
        // A Gravel Ride's car parks are those beside a Gravel Off-road network, and its km their green; a Trail
        // Ride's, those beside its hotspots (T189), or the Gravel networks' on a map built before them, so they
        // don't vanish until the next map update.
        var trail = activity is WendYourWay.Core.Loops.Activity.TrailRide && await HotspotTable.HasRowsAsync(dataSource, "trail_networks", cancellationToken);
        var (network, metres, only) =
            trail ? ("t.trail_network_id", "t.trail_network_m", "and t.trail_network_id is not null")
            : activity is WendYourWay.Core.Loops.Activity.GravelRide or WendYourWay.Core.Loops.Activity.TrailRide
                ? ("t.gravel_network_id", "t.gravel_network_m", "and t.gravel_network_id is not null")
                : ("t.network_id", "t.network_m", "");
        await using var query = dataSource.CreateCommand($"""
            with seen as (
                select ST_Transform(ST_MakeEnvelope(@west, @south, @east, @north, 4326), 27700) as box,
                       ST_Transform(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326), 27700) as middle)
            select t.id, {network}, {metres},
                   ST_Y(ST_Transform(t.geom, 4326)), ST_X(ST_Transform(t.geom, 4326)),
                   t.label, ST_Distance(t.geom, seen.middle), t.label <> 'Car park',
                   t.fee, t.capacity, t.hours, t.toilets, t.cafe, t.pub
              from network.trailheads t, seen
             where t.geom && seen.box
               -- The box's bounds in metres hold more than the map shows: only what it shows.
               and ST_Intersects(ST_Transform(t.geom, 4326), ST_MakeEnvelope(@west, @south, @east, @north, 4326))
               {only}
             order by {metres} desc, t.id
             limit @most
            """);
        query.Parameters.Add(new NpgsqlParameter("west", view.West));
        query.Parameters.Add(new NpgsqlParameter("south", view.South));
        query.Parameters.Add(new NpgsqlParameter("east", view.East));
        query.Parameters.Add(new NpgsqlParameter("north", view.North));
        query.Parameters.Add(new NpgsqlParameter("lon", middle.Longitude));
        query.Parameters.Add(new NpgsqlParameter("lat", middle.Latitude));
        query.Parameters.Add(new NpgsqlParameter("most", MostOnMap));

        var onMap = new List<FoundStart>();
        var near = new List<NearTrailhead>();
        var byId = new Dictionary<int, FoundStart>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var details = new CarParkDetails(
                reader.IsDBNull(8) ? null : Enum.Parse<CarParkFee>(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetBoolean(11), reader.GetBoolean(12), reader.GetBoolean(13));
            var found = new FoundStart(reader.GetString(5), new LatLon(reader.GetDouble(3), reader.GetDouble(4)), reader.GetFloat(2), details);
            var id = reader.GetInt32(0);
            onMap.Add(found);
            byId[id] = found;
            near.Add(new NearTrailhead(id, reader.GetInt32(1), reader.GetFloat(2), reader.GetDouble(6), reader.GetBoolean(7) ? found.Name : null));
        }
        return new CarParksAround(onMap, [.. Trailheads.Around(near, distanceKm).Select(t => byId[t.Id])]);
    }
}
