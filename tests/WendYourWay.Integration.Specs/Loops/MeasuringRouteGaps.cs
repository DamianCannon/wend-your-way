using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A measurement for T098 (2026-11-09): a route made from every Outing as the app makes one, from
/// every point it recorded, and the gaps in those routes where the Outing itself has none, so the
/// route matcher's, not the map's. Prints the Outing's name, its date, each gap's length and the
/// nearest named way; never a position. Read-only.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class MeasuringRouteGaps
{
    [Test]
    [Explicit("A measurement for T098")]
    public async Task Gaps_in_routes_made_from_Outings()
    {
        await using var probe = await RealNetwork.OpenOrIgnore(RealNetwork.GreatBritain);
        var readOnly = new NpgsqlConnectionStringBuilder(probe.ConnectionString) { Options = "-c default_transaction_read_only=on" };
        await using var app = Database.CreateDataSource(readOnly.ConnectionString);
        var outings = new List<(int Id, string Name, DateTimeOffset? Started)>();
        await using (var q = app.CreateCommand("select id, name, started_at from app.outings where source_file <> 't084-test' and matched_share >= 0.8 order by id"))
        await using (var r = await q.ExecuteReaderAsync())
        {
            var limit = int.TryParse(Environment.GetEnvironmentVariable("WEND_OUTINGS_LIMIT"), out var l) ? l : int.MaxValue;
            while (await r.ReadAsync() && outings.Count < limit) outings.Add((r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetFieldValue<DateTimeOffset>(2)));
        }

        var views = new PostgisOutingViews(app);
        var planner = new LoopPlanner(new PostgisNetworkSlices(app), new MapGeometry(app), ScoringWeights.Default, null, new RouteMatching(app));
        var found = new List<(string Outing, string Date, double LengthM, string Near)>();
        var (made, failed) = (0, 0);
        foreach (var (id, name, started) in outings)
        {
            var view = await views.ViewAsync(id);
            if (view is null) continue;
            PlannedLoop route;
            try
            {
                route = (await planner.ImportAsync(view.RouteTrack, view.Activity))[0];
            }
            catch (Exception e) when (e is RouteNotMatchedException or InvalidOperationException or ArgumentException)
            {
                failed++;
                continue;
            }
            made++;
            foreach (var gap in route.Gaps.Where(g => g.LengthM >= 50))
            {
                // Not one of the Outing's own gaps (off the map), and where it is in words.
                await using var q = app.CreateCommand("""
                    with g as (select ST_Transform(ST_SetSRID(ST_MakeLine(array(
                                 select ST_MakePoint(lon, lat) from unnest(@lons, @lats) with ordinality as p(lon, lat, ord) order by ord)), 4326), 27700) geom)
                    select exists (select 1 from app.outing_gaps og, g where og.outing_id = @id and ST_DWithin(og.geom, g.geom, 30)),
                           (select coalesce(w.tags->>'name', '') from osm.ways w, g
                             where w.geom && ST_Expand(ST_Transform(ST_Centroid(g.geom), 4326), 0.002) and w.tags ? 'highway' and w.tags ? 'name'
                             order by ST_Distance(ST_Transform(w.geom, 27700), ST_Centroid(g.geom)) limit 1)
                    """);
                q.Parameters.Add(new NpgsqlParameter("lons", gap.Line.Select(p => p.Longitude).ToArray()));
                q.Parameters.Add(new NpgsqlParameter("lats", gap.Line.Select(p => p.Latitude).ToArray()));
                q.Parameters.Add(new NpgsqlParameter("id", id));
                await using var r = await q.ExecuteReaderAsync();
                await r.ReadAsync();
                if (r.GetBoolean(0)) continue;
                found.Add((name, started?.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "", gap.LengthM, r.IsDBNull(1) ? "" : r.GetString(1)));
            }
        }
        await TestContext.Out.WriteLineAsync($"ROUTEGAPS {outings.Count} Outings, {made} made routes, {failed} couldn't be; {found.Count} gaps of 50 m or more off the Outings' own");
        foreach (var f in found.OrderByDescending(f => f.LengthM).Take(40))
            await TestContext.Out.WriteLineAsync(FormattableString.Invariant($"ROUTEGAPS {f.LengthM,6:0} m | {f.Outing} | {f.Date} | near {f.Near}"));
    }
}
