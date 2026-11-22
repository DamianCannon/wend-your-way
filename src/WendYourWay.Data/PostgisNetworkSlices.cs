using System.Globalization;
using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Data;

/// <summary>A Network built before an Activity's rules were: `wend network rebuild` adds them (T068, T021).</summary>
public sealed class NetworkNeedsRebuildException()
    : Exception("This Activity needs the map rebuilt first: run wend network rebuild.");

/// <summary>
/// Loads Network slices from network.segments, using its spatial index. A Trail Ride's leaves out
/// what OSM closes to motors in this month, by <paramref name="clock"/> (T021).
/// </summary>
public sealed class PostgisNetworkSlices(NpgsqlDataSource dataSource, TimeProvider? clock = null) : INetworkSlices
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    // How often reading a slice reports how far it has got: a 40 km slice has a million Segments.
    private const int ReportEvery = 5_000;

    public async Task<NetworkSlice> AroundAsync(NodePoint centre, double radiusM, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, Action<SliceReading>? reading = null, CancellationToken cancellationToken = default)
    {
        // Counted alongside on a connection of its own, so "n of N" costs no wait: a few seconds
        // into a 40 km slice's reading, a tenth of a second for 10 km (measured 2026-10-26).
        var progress = new Progress(reading);
        var riding = activity == WendYourWay.Core.Loops.Activity.GravelRide;
        var (access, usable) = AccessOf(activity);
        var counting = reading is null ? Task.CompletedTask : CountAsync(centre, radiusM, usable, progress, cancellationToken);

        // The roads each Segment runs beside, read whole alongside: all of Great Britain's pairs
        // take under a second, while picking out a slice's took 3-8 s more for 40 km, looked up
        // Segment by Segment (T055, measured 2026-10-26).
        var pairing = PairsAsync(cancellationToken);

        // The Obstacles, read whole alongside in the same way, for a Gravel Ride only: a Run pays
        // nothing for them (T069).
        var obstructing = riding ? ObstaclesAsync(cancellationToken) : Task.FromResult<(int[], ObstacleKind[], double[])>(([], [], []));

        // The Segments the user has Blocked (T082): forbidden to every Activity, at once.
        var blocking = BlockedAsync(cancellationToken);

        // A Run's Main Road junctions are split into corners, crossed at these (T145c): a bike turns
        // across a Main Road in its traffic, so a ride's aren't (user, 2026-11-16).
        var crossingAt = activity == WendYourWay.Core.Loops.Activity.Run ? CrossingPointsAsync(cancellationToken) : Task.FromResult<Dictionary<long, CrossingKind>?>(null);

        // The Activity's access, and only what it may use (AccessOf).
        await using var query = dataSource.CreateCommand($"""
            select s.id, s.osm_way_id, s.from_node_id, s.to_node_id,
                   ST_X(ST_StartPoint(s.geom)), ST_Y(ST_StartPoint(s.geom)),
                   ST_X(ST_EndPoint(s.geom)), ST_Y(ST_EndPoint(s.geom)),
                   s.length_m, s.ground::text, {access}::text,
                   s.main_road, s.greenery, s.corridor, s.waterside,
                   s.crossing, s.crossing_main_road, s.crossing_share, s.bridge_over_water, s.ascent_m, s.descent_m,
                   coalesce(s.carriageway, false), coalesce(s.busy, false), s.from_bearing, s.to_bearing, s.gap, s.pavement, s.customers
              from network.segments s
             where ST_DWithin(s.geom, ST_SetSRID(ST_MakePoint(@x, @y), 27700), @radius)
               {usable}
            """);
        query.Parameters.Add(new NpgsqlParameter("x", centre.X));
        query.Parameters.Add(new NpgsqlParameter("y", centre.Y));
        query.Parameters.Add(new NpgsqlParameter("radius", radiusM));

        var stored = new List<StoredSegment>();
        // Each row's place in the slice, by its id in the Network.
        var rowOf = new Dictionary<int, int>();
        // A Corridor's name repeats along it: keep one copy per slice.
        var corridors = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (stored.Count % ReportEvery == 0 && stored.Count > 0) progress.Read(stored.Count);
                rowOf[reader.GetInt32(0)] = stored.Count;
                stored.Add(new StoredSegment(
                    new SegmentKey(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)),
                    new NodePoint(reader.GetDouble(4), reader.GetDouble(5)),
                    new NodePoint(reader.GetDouble(6), reader.GetDouble(7)),
                    reader.GetDouble(8),
                    Ground(reader.GetString(9)),
                    reader.IsDBNull(10) ? throw new NetworkNeedsRebuildException() : Access(reader.GetString(10)),
                    null,
                    reader.GetBoolean(11),
                    reader.GetFloat(12),
                    reader.IsDBNull(13) ? null : Corridor(reader.GetString(13)),
                    reader.GetFloat(14),
                    reader.IsDBNull(15) ? null : Crossing(reader.GetString(15)),
                    reader.GetBoolean(16),
                    reader.GetFloat(17),
                    reader.GetBoolean(18),
                    reader.GetFloat(19),
                    reader.GetFloat(20))
                {
                    Carriageway = reader.GetBoolean(21),
                    Busy = reader.GetBoolean(22),
                    FromBearing = reader.IsDBNull(23) ? null : reader.GetFloat(23),
                    ToBearing = reader.IsDBNull(24) ? null : reader.GetFloat(24),
                    MapGap = reader.IsDBNull(25) ? MapGapKind.None : reader.GetString(25) == "joined" ? MapGapKind.Joined : MapGapKind.Possible,
                    // A real pavement (T181) is a Run's: on a bike it's the road.
                    Pavement = activity == WendYourWay.Core.Loops.Activity.Run && reader.GetBoolean(26),
                    Customers = reader.GetBoolean(27),
                });
                // A possible gap (T146) is used only when the user includes it: not by any search.
                if (stored[^1].MapGap == MapGapKind.Possible) stored[^1] = stored[^1] with { Access = AccessKind.Forbidden };
            }
        }
        Beside(stored, rowOf, await pairing);
        Obstructed(stored, rowOf, await obstructing);
        var blocked = await blocking;
        if (blocked.Count > 0)
        {
            for (var i = 0; i < stored.Count; i++)
                if (blocked.Contains(stored[i].Key)) stored[i] = stored[i] with { Access = AccessKind.Forbidden };
        }
        await counting;
        progress.Read(stored.Count, done: true);
        return NetworkSlice.Assemble(stored, await crossingAt);

        static CrossingKind Crossing(string kind) => kind switch
        {
            "signals" => CrossingKind.Signals,
            "marked" => CrossingKind.Marked,
            _ => CrossingKind.Unmarked,
        };

        string Corridor(string name)
        {
            if (corridors.TryGetValue(name, out var kept)) return kept;
            corridors.Add(name, name);
            return name;
        }
    }

    // Each Activity's access column, and what of the slice it may use (a Run reads all). A Gravel
    // Ride's or Trail Ride's slice holds only what it may use: about half a city's Segments are
    // footways (T068). One not yet given the Activity's access (null) is kept, to be refused.
    private (string Access, string Usable) AccessOf(WendYourWay.Core.Loops.Activity activity) => activity switch
    {
        WendYourWay.Core.Loops.Activity.GravelRide => ("s.access_ride", "and s.access_ride is distinct from 'forbidden'"),
        WendYourWay.Core.Loops.Activity.TrailRide => ("s.access_trail",
            "and s.access_trail is distinct from 'forbidden' and coalesce(s.closed_months, 0) & " +
            (1 << (_clock.GetLocalNow().Month - 1)).ToString(CultureInfo.InvariantCulture) + " = 0"),
        _ => ("s.access_run", ""),
    };

    private async Task CountAsync(NodePoint centre, double radiusM, string usable, Progress progress, CancellationToken cancellationToken)
    {
        await using var count = dataSource.CreateCommand(
            "select count(*) from network.segments s where ST_DWithin(s.geom, ST_SetSRID(ST_MakePoint(@x, @y), 27700), @radius) " + usable);
        count.Parameters.Add(new NpgsqlParameter("x", centre.X));
        count.Parameters.Add(new NpgsqlParameter("y", centre.Y));
        count.Parameters.Add(new NpgsqlParameter("radius", radiusM));
        progress.Counted(Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture));
    }

    // Reports from the reading and the counting, one at a time.
    private sealed class Progress(Action<SliceReading>? reading)
    {
        private readonly Lock _gate = new();
        private int _read;
        private int? _total;

        public void Read(int read, bool done = false)
        {
            if (reading is null) return;
            lock (_gate)
            {
                _read = read;
                // Whatever the count said, all there is has been read.
                if (done) _total = read;
                reading(new SliceReading(_read, _total));
            }
        }

        public void Counted(int total)
        {
            if (reading is null) return;
            lock (_gate)
            {
                _total = total;
                reading(new SliceReading(_read, _total));
            }
        }
    }

    // Every Segment beside a road, the road, and for how many metres, in all the Network.
    private async Task<(int[] Paths, int[] Roads, double[] Metres)> PairsAsync(CancellationToken cancellationToken)
    {
        await using var query = dataSource.CreateCommand("select path_id, road_id, metres from network.segment_beside");
        var (paths, roads, metres) = (new List<int>(), new List<int>(), new List<double>());
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            paths.Add(reader.GetInt32(0));
            roads.Add(reader.GetInt32(1));
            metres.Add(reader.GetDouble(2));
        }
        return ([.. paths], [.. roads], [.. metres]);
    }

    // The crossing points mapped on Main Roads, by OSM node, in all the Network: none on one built
    // before T145, which isn't split.
    private async Task<Dictionary<long, CrossingKind>?> CrossingPointsAsync(CancellationToken cancellationToken)
    {
        await using var query = dataSource.CreateCommand("select node_id, kind from network.crossing_nodes");
        var points = new Dictionary<long, CrossingKind>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            points[reader.GetInt64(0)] = reader.GetString(1) switch
            {
                "signals" => CrossingKind.Signals,
                "marked" => CrossingKind.Marked,
                _ => CrossingKind.Unmarked,
            };
        return points;
    }

    // The Segments the user has Blocked, by OSM identity; none on a database before Marks.
    private async Task<HashSet<SegmentKey>> BlockedAsync(CancellationToken cancellationToken)
    {
        await using (var exists = dataSource.CreateCommand("select to_regclass('app.marks') is not null"))
        {
            if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken))!) return [];
        }
        await using var query = dataSource.CreateCommand("select osm_way_id, from_node_id, to_node_id from app.marks where kind = 'blocked'");
        var blocked = new HashSet<SegmentKey>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) blocked.Add(new SegmentKey(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
        return blocked;
    }

    // Every Segment's Obstacles in all the Network: a few hundred thousand rows for Great Britain.
    private async Task<(int[] Segments, ObstacleKind[] Kinds, double[] Shares)> ObstaclesAsync(CancellationToken cancellationToken)
    {
        await using var query = dataSource.CreateCommand("select segment_id, kind, share from network.segment_obstacles");
        var (segments, kinds, shares) = (new List<int>(), new List<ObstacleKind>(), new List<double>());
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            segments.Add(reader.GetInt32(0));
            kinds.Add(Obstacle(reader.GetString(1)));
            shares.Add(reader.GetFloat(2));
        }
        return ([.. segments], [.. kinds], [.. shares]);
    }

    // Gives each Segment in the slice its Obstacles, in the order of their kinds.
    private static void Obstructed(List<StoredSegment> stored, Dictionary<int, int> rowOf, (int[] Segments, ObstacleKind[] Kinds, double[] Shares) obstacles)
    {
        var on = new Dictionary<int, List<ObstacleShare>>();
        for (var i = 0; i < obstacles.Segments.Length; i++)
        {
            if (!rowOf.TryGetValue(obstacles.Segments[i], out var row)) continue;
            (on.TryGetValue(row, out var shares) ? shares : on[row] = []).Add(new ObstacleShare(obstacles.Kinds[i], obstacles.Shares[i]));
        }
        foreach (var (row, shares) in on) stored[row] = stored[row] with { Obstacles = [.. shares.OrderBy(s => s.Kind)] };
    }

    private static ObstacleKind Obstacle(string kind) => kind switch
    {
        "gate" => ObstacleKind.Gate,
        "cycle_barrier" => ObstacleKind.CycleBarrier,
        "kissing_gate" => ObstacleKind.KissingGate,
        "turnstile" => ObstacleKind.Turnstile,
        "motorcycle_barrier" => ObstacleKind.MotorcycleBarrier,
        "stile" => ObstacleKind.Stile,
        "cattle_grid" => ObstacleKind.CattleGrid,
        "ford" => ObstacleKind.Ford,
        _ => throw new InvalidOperationException($"Unknown Obstacle '{kind}' in network.segment_obstacles."),
    };

    // Gives each Segment in the slice the roads it runs beside, in the order of their ids, as the
    // Network's own index keeps them. A road in the slice is named by its identity; one outside
    // it by a stand-in no Segment can have, so the Segments beside it still share its ground.
    private static void Beside(List<StoredSegment> stored, Dictionary<int, int> rowOf, (int[] Paths, int[] Roads, double[] Metres) pairs)
    {
        var beside = new Dictionary<int, List<(int Road, double Metres)>>();
        for (var i = 0; i < pairs.Paths.Length; i++)
        {
            if (!rowOf.TryGetValue(pairs.Paths[i], out var row)) continue;
            (beside.TryGetValue(row, out var roads) ? roads : beside[row] = []).Add((pairs.Roads[i], pairs.Metres[i]));
        }
        foreach (var (row, roads) in beside)
        {
            stored[row] = stored[row] with
            {
                Beside = [.. roads.OrderBy(r => r.Road).Select(r => new RoadShare(
                    rowOf.TryGetValue(r.Road, out var road) ? stored[road].Key : new SegmentKey(-r.Road, 0, 0), r.Metres))],
            };
        }
    }

    private static GroundKind Ground(string ground) => ground switch
    {
        "off_road" => GroundKind.OffRoad,
        "traffic_free" => GroundKind.TrafficFree,
        "connector" => GroundKind.Connector,
        _ => throw new InvalidOperationException($"Unknown ground '{ground}' in network.segments."),
    };

    private static AccessKind Access(string access) => access switch
    {
        "allowed" => AccessKind.Allowed,
        "unconfirmed" => AccessKind.Unconfirmed,
        "push_only" => AccessKind.PushOnly,
        "forbidden" => AccessKind.Forbidden,
        _ => throw new InvalidOperationException($"Unknown access '{access}' in network.segments."),
    };
}
