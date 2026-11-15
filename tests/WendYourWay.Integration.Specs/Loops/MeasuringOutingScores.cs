using Stopwatch = System.Diagnostics.Stopwatch;
using Process = System.Diagnostics.Process;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// STORY-045, measured first (scratch): whether every Outing can be matched onto the Network and
/// scored as a Loop is, how long that takes, and how the scores spread, so the app's stars can be
/// drawn from them. Aggregates only: no Outing's name, place or track is printed. Read-only.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class MeasuringOutingScores
{
    [Test]
    [Explicit("A measurement for STORY-045")]
    public async Task Scoring_every_Outing()
    {
        var name = Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.GreatBritain;
        var limit = int.TryParse(Environment.GetEnvironmentVariable("WEND_OUTINGS_LIMIT"), out var l) ? l : int.MaxValue;
        await using var probe = await RealNetwork.OpenOrIgnore(name);
        var readOnly = new NpgsqlConnectionStringBuilder(probe.ConnectionString) { Options = "-c default_transaction_read_only=on" };
        await using var dataSource = Database.CreateDataSource(readOnly.ConnectionString);

        var outings = new List<(int Id, Activity Activity, LatLon[] Track)>();
        await using (var command = dataSource.CreateCommand("""
            select id, coalesce(activity::text, 'gravel_ride'),
                   array(select ST_X(d.geom) from ST_DumpPoints(ST_Transform(geom, 4326)) d order by d.path),
                   array(select ST_Y(d.geom) from ST_DumpPoints(ST_Transform(geom, 4326)) d order by d.path)
              from app.outings where source_file <> 't084-test' order by id
            """))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync() && outings.Count < limit)
            {
                var (lons, lats) = (reader.GetFieldValue<double[]>(2), reader.GetFieldValue<double[]>(3));
                outings.Add((reader.GetInt32(0), reader.GetString(1) == "run" ? Activity.Run : Activity.GravelRide,
                    [.. lons.Zip(lats, (lon, lat) => new LatLon(lat, lon))]));
            }
        }

        var matching = new RouteMatching(dataSource);
        var results = new List<(Activity Activity, double Score, double Seconds, double MatchedShare, bool Closed)>();
        var failures = new Dictionary<string, int>();
        var clock = Stopwatch.StartNew();
        foreach (var (_, activity, track) in outings)
        {
            var started = clock.Elapsed;
            try
            {
                var closed = Metres(track[0], track[^1]) <= 200;
                var matched = await matching.MatchAsync(track, activity, 200, closed, 45_000);
                var score = LoopScorer.Score(matched.Slice, matched.Match.Loop, ScoringWeights.Default.For(activity));
                results.Add((activity, score, (clock.Elapsed - started).TotalSeconds, matched.Match.LengthM / matched.RouteLengthM, closed));
            }
            catch (Exception e) when (e is RouteNotMatchedException or InvalidOperationException or ArgumentException)
            {
                var kind = e.GetType().Name;
                failures[kind] = failures.GetValueOrDefault(kind) + 1;
            }
        }

        static string Percentiles(IEnumerable<double> values)
        {
            var sorted = values.Order().ToArray();
            if (sorted.Length == 0) return "none";
            double At(double p) => sorted[(int)Math.Min(sorted.Length - 1, Math.Round(p * (sorted.Length - 1)))];
            return FormattableString.Invariant($"min {sorted[0]:0.000} p10 {At(0.1):0.000} p25 {At(0.25):0.000} p50 {At(0.5):0.000} p75 {At(0.75):0.000} p90 {At(0.9):0.000} max {sorted[^1]:0.000}");
        }

        await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
            $"OUTINGS {outings.Count} read, {results.Count} scored, {results.Count(r => r.Closed)} back at their start; failed: {string.Join(", ", failures.Select(f => $"{f.Key} {f.Value}"))}; {clock.Elapsed.TotalMinutes:0.0} min in all"));
        await TestContext.Out.WriteLineAsync($"OUTINGS seconds each: {Percentiles(results.Select(r => r.Seconds))}");
        await TestContext.Out.WriteLineAsync($"OUTINGS matched length share: {Percentiles(results.Select(r => r.MatchedShare))}");
        foreach (var group in results.GroupBy(r => r.Activity))
            await TestContext.Out.WriteLineAsync($"OUTINGS score, {group.Key} ({group.Count()}): {Percentiles(group.Select(r => r.Score))}");
        // The fifths' cut points (T097), from those matched for at least 80% of their length.
        foreach (var group in results.Where(r => r.MatchedShare >= 0.8 && !double.IsNaN(r.Score)).GroupBy(r => r.Activity))
        {
            var sorted = group.Select(r => r.Score).Order().ToArray();
            double At(double p) => sorted[(int)Math.Min(sorted.Length - 1, Math.Round(p * (sorted.Length - 1)))];
            await TestContext.Out.WriteLineAsync(FormattableString.Invariant(
                $"OUTINGS fifths, {group.Key} ({sorted.Length} matched for 80% or more): p20 {At(0.2):0.0000} p40 {At(0.4):0.0000} p60 {At(0.6):0.0000} p80 {At(0.8):0.0000}"));
        }
        await TestContext.Out.WriteLineAsync($"OUTINGS matched for less than 80%: {results.Count(r => r.MatchedShare < 0.8)}, NaN scores {results.Count(r => double.IsNaN(r.Score))}");
        await TestContext.Out.WriteLineAsync($"OUTINGS process peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1e9:0.0} GB");
    }

    private static double Metres(LatLon a, LatLon b)
    {
        const double MetresPerDegree = 111_320;
        var north = (a.Latitude - b.Latitude) * MetresPerDegree;
        var east = (a.Longitude - b.Longitude) * MetresPerDegree * Math.Cos((a.Latitude + b.Latitude) / 2 * Math.PI / 180);
        return Math.Sqrt(north * north + east * east);
    }
}
