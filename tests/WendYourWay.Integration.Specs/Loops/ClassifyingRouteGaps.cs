using WendYourWay.Core.Loops;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A measurement for T098 (2026-11-10): each Outing's track matched as a route is made from it, and
/// each stretch the matcher couldn't join told apart: with nothing to join it in the corridor, or
/// only a long way round; near where the Outing starts and finishes, or not; and, for a ride, gone
/// when matched with a Run's access. Outings that have a gap of their own there aren't counted.
/// Counts only. Read-only.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class ClassifyingRouteGaps
{
    [Test]
    [Explicit("A measurement for T098")]
    public async Task Why_routes_made_from_Outings_have_gaps()
    {
        await using var probe = await RealNetwork.OpenOrIgnore(RealNetwork.GreatBritain);
        var readOnly = new NpgsqlConnectionStringBuilder(probe.ConnectionString) { Options = "-c default_transaction_read_only=on" };
        await using var app = Database.CreateDataSource(readOnly.ConnectionString);
        var limit = int.TryParse(Environment.GetEnvironmentVariable("WEND_OUTINGS_LIMIT"), out var l) ? l : int.MaxValue;

        var outings = new List<(int Id, Activity Activity, LatLon[] Track)>();
        await using (var q = app.CreateCommand("""
            select id, activity::text,
                   array(select ST_Y(d.geom) from ST_DumpPoints(ST_Transform(geom, 4326)) d order by d.path),
                   array(select ST_X(d.geom) from ST_DumpPoints(ST_Transform(geom, 4326)) d order by d.path)
              from app.outings where source_file <> 't084-test' and matched_share >= 0.8 order by id
            """))
        await using (var r = await q.ExecuteReaderAsync())
        {
            while (await r.ReadAsync() && outings.Count < limit)
            {
                var (lats, lons) = (r.GetFieldValue<double[]>(2), r.GetFieldValue<double[]>(3));
                LatLon[] track = [.. lats.Zip(lons, (a, b) => new LatLon(a, b)).Where((p, i) => i == 0 || p != new LatLon(lats[i - 1], lons[i - 1]))];
                outings.Add((r.GetInt32(0), r.IsDBNull(1) || r.GetString(1) != "run" ? Activity.GravelRide : Activity.Run, track));
            }
        }

        var matching = new RouteMatching(app);
        var counts = new Dictionary<string, (int Count, double Metres)>();
        void Count(string kind, double m) => counts[kind] = (counts.GetValueOrDefault(kind).Count + 1, counts.GetValueOrDefault(kind).Metres + m);
        foreach (var (id, activity, track) in outings)
        {
            var closed = Metres(track[0], track[^1]) <= 200;
            MatchedRoute matched;
            try
            {
                matched = await matching.MatchAsync(track, activity, 200, closed, 45_000);
            }
            catch (Exception e) when (e is RouteNotMatchedException or InvalidOperationException or ArgumentException)
            {
                Count("not matched", 0);
                continue;
            }
            var along = new double[track.Length];
            for (var i = 1; i < track.Length; i++) along[i] = along[i - 1] + Metres(track[i - 1], track[i]);
            HashSet<int>? asRun = null;
            foreach (var gap in matched.Match.Unjoined.Where(u => u.RouteM >= 50))
            {
                // One of the Outing's own gaps, off the map: not the matcher's.
                var at = track[Math.Min(gap.RoutePoint, track.Length - 1)];
                await using (var q = app.CreateCommand("select exists (select 1 from app.outing_gaps g where g.outing_id = @id and ST_DWithin(g.geom, ST_Transform(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326), 27700), 60))"))
                {
                    q.Parameters.Add(new NpgsqlParameter("id", id));
                    q.Parameters.Add(new NpgsqlParameter("lon", at.Longitude));
                    q.Parameters.Add(new NpgsqlParameter("lat", at.Latitude));
                    if ((bool)(await q.ExecuteScalarAsync())!) { Count("off the map (the Outing's own)", gap.RouteM); continue; }
                }
                // Beside what: ways this Activity may use, only ways it may not, or neither.
                var end = gap.RoutePoint;
                while (end + 1 < along.Length && along[end] - along[gap.RoutePoint] < gap.RouteM) end++;
                var stretch = track[gap.RoutePoint..(end + 1)];
                string beside;
                await using (var q = app.CreateCommand($"""
                    with t as (select ST_Transform(ST_SetSRID(ST_MakeLine(array(
                                 select ST_MakePoint(lon, lat) from unnest(@lons, @lats) with ordinality as p(lon, lat, ord) order by ord)), 4326), 27700) g),
                    x as (select (sample_line(t.g, 5)).point p from t)
                    select count(*) filter (where exists (select 1 from network.segments s where ST_DWithin(s.geom, x.p, 15) and s.{(activity == Activity.Run ? "access_run" : "access_ride")} <> 'forbidden')),
                           count(*) filter (where not exists (select 1 from network.segments s where ST_DWithin(s.geom, x.p, 15) and s.{(activity == Activity.Run ? "access_run" : "access_ride")} <> 'forbidden')
                                              and exists (select 1 from network.segments s where ST_DWithin(s.geom, x.p, 15))),
                           count(*),
                           max((select min(ST_Distance(s.geom, x.p)) from network.segments s where ST_DWithin(s.geom, x.p, 60) and s.{(activity == Activity.Run ? "access_run" : "access_ride")} <> 'forbidden'))
                      from x
                    """))
                {
                    q.Parameters.Add(new NpgsqlParameter("lons", stretch.Select(p => p.Longitude).ToArray()));
                    q.Parameters.Add(new NpgsqlParameter("lats", stretch.Select(p => p.Latitude).ToArray()));
                    await using var r = await q.ExecuteReaderAsync();
                    await r.ReadAsync();
                    var (usable, forbiddenOnly, all) = (r.GetInt64(0), r.GetInt64(1), Math.Max(1, r.GetInt64(2)));
                    var farthest = r.IsDBNull(3) ? 999 : r.GetDouble(3);
                    Count($"  (strays) farthest from a usable way {(farthest <= 10 ? "10 m or less" : farthest <= 20 ? "10-20 m" : farthest <= 25 ? "20-25 m" : farthest <= 60 ? "25-60 m" : "over 60 m")}", gap.RouteM);
                    beside = forbiddenOnly > 0.3 * all ? "beside ways it may not use" : usable > 0.9 * all ? "beside usable ways all along" : "partly beside nothing";
                }
                Count($"  ({activity}) {beside}", gap.RouteM);
                var nearEnds = along[Math.Min(gap.RoutePoint, along.Length - 1)] < 300 || along[^1] - along[Math.Min(gap.RoutePoint, along.Length - 1)] - gap.RouteM < 300;
                var why = gap.JoinedM is null ? "nothing in the corridor" : "only the long way round";
                if (gap.JoinedM is null && gap.From >= 0 && gap.To >= 0)
                {
                    var path = WendYourWay.Core.Routing.PathFinder.Find(matched.Slice, WendYourWay.Core.Routing.PathEnd.AtNode(gap.From), WendYourWay.Core.Routing.PathEnd.AtNode(gap.To), new Shortest());
                    Count(path is null ? "  [corridor] none in the whole slice either: not joined on the map"
                        : path.LengthM <= (1.5 * gap.RouteM) + 50 ? "  [corridor] a short way outside the corridor: the matcher's"
                        : "  [corridor] only a long way in the whole slice", gap.RouteM);
                    Count($"  [corridor] junctions on {await KindsAsync(app, matched.Slice, gap.From, gap.To)}", gap.RouteM);
                }
                Count($"{activity}, {why}{(nearEnds ? ", at its ends" : "")}", gap.RouteM);
                if (activity == Activity.GravelRide)
                {
                    asRun ??= await GapsAsRunAsync(matching, track, closed);
                    Count(asRun.Any(p => Math.Abs(p - gap.RoutePoint) < 30) ? "  (ride) still a gap as a Run" : "  (ride) gone as a Run: access", gap.RouteM);
                }
            }
        }
        await TestContext.Out.WriteLineAsync($"CLASSIFY {outings.Count} Outings");
        foreach (var (kind, (count, metres)) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
            await TestContext.Out.WriteLineAsync(FormattableString.Invariant($"CLASSIFY {kind,-55} {count,5} gaps {metres / 1000,8:0.0} km"));
    }

    // Only length: is there any way at all between two junctions.
    private sealed class Shortest : WendYourWay.Core.Routing.ICostModel
    {
        public double FloorPerMetre => 1;

        public double CostOf(WendYourWay.Core.Network.Segment segment, bool forward) => segment.LengthM;
    }

    // The kind of way each junction is on: a road or a path. No name or place.
    private static async Task<string> KindsAsync(NpgsqlDataSource app, WendYourWay.Core.Network.NetworkSlice slice, int from, int to)
    {
        long WayAt(int junction) => slice.Segments.First(s => s.From == junction || s.To == junction).Key.OsmWayId;
        var (a, b) = (WayAt(from), WayAt(to));
        await using var q = app.CreateCommand("""
            select w.way_id, w.tags->>'highway' from osm.ways w where w.way_id = any(@ids)
            """);
        q.Parameters.Add(new NpgsqlParameter("ids", new[] { a, b }));
        var ways = new Dictionary<long, string>();
        await using (var r = await q.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) ways[r.GetInt64(0)] = r.GetString(1);
        }
        static string Kind(string highway) => highway is "footway" or "path" or "cycleway" or "bridleway" or "steps" or "track" or "pedestrian" ? "path" : "road";
        if (!ways.TryGetValue(a, out var wa) || !ways.TryGetValue(b, out var wb)) return "unknown";
        return string.Join("-", new[] { Kind(wa), Kind(wb) }.Order(StringComparer.Ordinal)) + (a == b ? " (one way)" : "");
    }

    private static async Task<HashSet<int>> GapsAsRunAsync(RouteMatching matching, LatLon[] track, bool closed)
    {
        try
        {
            var matched = await matching.MatchAsync(track, Activity.Run, 200, closed, 45_000);
            return [.. matched.Match.Unjoined.Where(u => u.RouteM >= 50).Select(u => u.RoutePoint)];
        }
        catch (Exception e) when (e is RouteNotMatchedException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    private static double Metres(LatLon a, LatLon b)
    {
        const double MetresPerDegree = 111_320;
        var north = (a.Latitude - b.Latitude) * MetresPerDegree;
        var east = (a.Longitude - b.Longitude) * MetresPerDegree * Math.Cos((a.Latitude + b.Latitude) / 2 * Math.PI / 180);
        return Math.Sqrt(north * north + east * east);
    }
}
