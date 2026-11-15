using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A measurement for T100 (2026-11-10): how many turn points a Loop would have, per km, at each
/// threshold of turning, so a twisting path doesn't speak at every bend. Only a junction with a
/// choice (three ways or more) can need one; there, the bearing the Loop leaves by against the one it
/// came in on, each over the 15 m either side. On the user's rated Loops, matched onto London's
/// Network as the guard matches them. Counts only. Read-only.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class MeasuringTurnPoints
{
    private static readonly int[] Thresholds = [30, 45, 60];
    private static readonly double[] Reaches = [30, 50];

    [Test]
    [Explicit("A measurement for T100")]
    public async Task Turn_points_per_km()
    {
        var folder = Environment.GetEnvironmentVariable("WEND_REFERENCE_LOOPS");
        if (folder is null || !Directory.Exists(folder)) Assert.Ignore("No rated Loops here: set WEND_REFERENCE_LOOPS.");
        await using var probe = await RealNetwork.OpenOrIgnore(RealNetwork.London);
        var readOnly = new NpgsqlConnectionStringBuilder(probe.ConnectionString) { Options = "-c default_transaction_read_only=on" };
        await using var london = Database.CreateDataSource(readOnly.ConnectionString);
        var matching = new RouteMatching(london);

        var perKm = Reaches.SelectMany(r => Thresholds.Select(t => (r, t))).ToDictionary(k => k, _ => new List<double>());
        var choices = new List<double>();
        foreach (var file in Directory.EnumerateFiles(folder!, "*.gpx").Order(StringComparer.Ordinal))
        {
            LatLon[] route = [.. RecordedTrack.Read(file).Points.Select(p => new LatLon(p.Latitude, p.Longitude))];
            MatchedRoute matched;
            try
            {
                matched = await matching.MatchAsync(route);
            }
            catch (Exception e) when (e is RouteNotMatchedException or InvalidOperationException or ArgumentException)
            {
                continue;
            }
            var slice = matched.Slice;
            var traversals = matched.Match.Loop.Traversals;
            if (traversals.Count < 2) continue;
            var km = traversals.Sum(t => slice.Segments[t.Segment].LengthM) / 1000;
            if (km < 1) continue;

            var keys = traversals.Select(t => (Key: slice.Segments[t.Segment].Key, t.Forward)).ToArray();
            await using var q = london.CreateCommand("""
                with t as (
                  select ord, way, f, tt, fwd from unnest(@ways, @froms, @tos, @fwds) with ordinality as x(way, f, tt, fwd, ord))
                select t.ord,
                       array(select ST_X(d.geom) from ST_DumpPoints(case when t.fwd then s.geom else ST_Reverse(s.geom) end) d order by d.path),
                       array(select ST_Y(d.geom) from ST_DumpPoints(case when t.fwd then s.geom else ST_Reverse(s.geom) end) d order by d.path),
                       (select count(*) from network.segments s2 where s2.from_node_id = case when t.fwd then t.tt else t.f end
                                                               or s2.to_node_id = case when t.fwd then t.tt else t.f end)
                  from t join network.segments s on s.osm_way_id = t.way and s.from_node_id = t.f and s.to_node_id = t.tt
                 order by t.ord
                """);
            q.Parameters.Add(new NpgsqlParameter("ways", keys.Select(k => k.Key.OsmWayId).ToArray()));
            q.Parameters.Add(new NpgsqlParameter("froms", keys.Select(k => k.Key.FromNodeId).ToArray()));
            q.Parameters.Add(new NpgsqlParameter("tos", keys.Select(k => k.Key.ToNodeId).ToArray()));
            q.Parameters.Add(new NpgsqlParameter("fwds", keys.Select(k => k.Forward).ToArray()));
            var line = new List<(double X, double Y)>();
            var junctions = new List<(int Point, long Ways)>();
            await using (var r = await q.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var (xs, ys) = (r.GetFieldValue<double[]>(1), r.GetFieldValue<double[]>(2));
                    for (var i = line.Count == 0 ? 0 : 1; i < xs.Length; i++) line.Add((xs[i], ys[i]));
                    junctions.Add((line.Count - 1, r.GetInt64(3)));
                }
            }
            var along = new double[line.Count];
            for (var i = 1; i < line.Count; i++) along[i] = along[i - 1] + Math.Sqrt(Math.Pow(line[i].X - line[i - 1].X, 2) + Math.Pow(line[i].Y - line[i - 1].Y, 2));
            (double X, double Y) At(double d)
            {
                d = Math.Clamp(d, 0, along[^1]);
                var i = Array.BinarySearch(along, d);
                if (i >= 0) return line[i];
                i = ~i;
                var f = (d - along[i - 1]) / (along[i] - along[i - 1]);
                return (line[i - 1].X + (f * (line[i].X - line[i - 1].X)), line[i - 1].Y + (f * (line[i].Y - line[i - 1].Y)));
            }
            static double Bearing((double X, double Y) a, (double X, double Y) b) => Math.Atan2(b.X - a.X, b.Y - a.Y) * 180 / Math.PI;
            foreach (var reach in Reaches)
            {
                var turns = new List<(double At, double Turn)>();
                foreach (var (point, ways) in junctions.Where(j => j.Ways >= 3 && j.Point < line.Count - 1))
                {
                    var d = along[point];
                    var turn = Math.Abs(((Bearing(At(d), At(d + reach)) - Bearing(At(d - reach), At(d)) + 540) % 360) - 180);
                    // Junctions closer than the reach are one: the sharpest of them speaks.
                    if (turns.Count > 0 && d - turns[^1].At < reach) { if (turn > turns[^1].Turn) turns[^1] = (d, turn); }
                    else turns.Add((d, turn));
                }
                foreach (var t in Thresholds) perKm[(reach, t)].Add(turns.Count(x => x.Turn >= t) / km);
            }
            choices.Add(junctions.Count(j => j.Ways >= 3) / km);
        }

        static string Spread(List<double> v)
        {
            var s = v.Order().ToArray();
            double At(double p) => s[(int)Math.Round(p * (s.Length - 1))];
            return FormattableString.Invariant($"median {At(0.5):0.0}, p10 {At(0.1):0.0}, p90 {At(0.9):0.0}, most {s[^1]:0.0}");
        }
        await TestContext.Out.WriteLineAsync($"TURNS {choices.Count} rated Loops; junctions with a choice per km: {Spread(choices)}");
        foreach (var reach in Reaches)
            foreach (var t in Thresholds)
                await TestContext.Out.WriteLineAsync($"TURNS over {reach} m either side, turning {t} degrees or more, per km: {Spread(perKm[(reach, t)])}");
    }
}
