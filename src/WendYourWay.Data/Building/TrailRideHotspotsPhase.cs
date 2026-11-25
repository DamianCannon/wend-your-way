using System.Globalization;
using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>What finding the Trail Ride hotspots did (T189): how many were kept, and the largest's metres.</summary>
public sealed record TrailRideHotspotBuild(long Kept, double LargestM);

/// <summary>
/// The build's Trail Ride hotspots phase (T189, #193): the off-road ways a Trail Ride may use (not the unconfirmed),
/// grouped where they lie within <see cref="WithinM"/> of one another, whatever lies between: a motorbike may use
/// every road, so joining them through roads, as the Gravel networks are, would join them all. Those of
/// <see cref="KeepFromM"/> or more kept, each marked at its own junction nearest its middle by length; and a Trail
/// Ride's car parks found beside them. The spike (T187) found 53 of 10 km or more across England and Wales, the
/// largest Salisbury Plain's, 256 km; at 3 km apart, they began to run together.
/// </summary>
public static class TrailRideHotspotsPhase
{
    /// <summary>How near byways must lie to be one hotspot.</summary>
    public const double WithinM = 2_000;

    /// <summary>The least kept: below a hotspot's shown, so that line can move without a build.</summary>
    public const double KeepFromM = 5_000;

    /// <summary>
    /// Finds the Trail Ride hotspots of the Network in <paramref name="schema"/> (network_next in a build, network on
    /// its own) in place of any found before, within <paramref name="transaction"/>. After the Trailheads phase.
    /// </summary>
    public static async Task<TrailRideHotspotBuild> Build(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema,
        CancellationToken cancellationToken, double withinM = WithinM, double keepFromM = KeepFromM)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (schema is not ("network" or "network_next")) throw new ArgumentOutOfRangeException(nameof(schema), schema, "Only network or network_next.");
        string M(double metres) => metres.ToString(CultureInfo.InvariantCulture);

        await Execute($"""
            drop table if exists trail_ways;
            create temp table trail_ways on commit drop as
            select id, geom, length_m, ST_ClusterDBSCAN(geom, eps := {M(withinM)}, minpoints := 1) over () as grouped
              from {schema}.segments
             where ground = 'off_road' and access_trail = 'allowed';

            -- Each kept, largest first, so the ids say their order: its middle by length, its box, and its own
            -- junction nearest that middle.
            drop table if exists trail_kept;
            create temp table trail_kept on commit drop as
            select row_number() over (order by metres desc, grouped)::integer as id, grouped, metres, box,
                   (select p from (select ST_StartPoint(w.geom) as p from trail_ways w where w.grouped = g.grouped
                                   union all select ST_EndPoint(w.geom) from trail_ways w where w.grouped = g.grouped) ends
                     order by p <-> g.middle limit 1) as at
              from (select grouped, sum(length_m) as metres,
                           ST_MakeEnvelope(min(ST_XMin(geom)), min(ST_YMin(geom)), max(ST_XMax(geom)), max(ST_YMax(geom)), 27700) as box,
                           ST_SetSRID(ST_MakePoint(sum(ST_X(ST_LineInterpolatePoint(geom, 0.5)) * length_m) / sum(length_m),
                                                   sum(ST_Y(ST_LineInterpolatePoint(geom, 0.5)) * length_m) / sum(length_m)), 27700) as middle
                      from trail_ways group by grouped having sum(length_m) >= {M(keepFromM)}) g;

            truncate {schema}.trail_networks;
            insert into {schema}.trail_networks (id, metres, at, box) select id, metres, at, box from trail_kept;

            -- A Trail Ride's car parks: each Trailhead's largest kept group with a way within its reach, or none.
            update {schema}.trailheads t
               set (trail_network_id, trail_network_m) = (
                   select k.id, k.metres
                     from trail_ways w join trail_kept k on k.grouped = w.grouped
                    where ST_DWithin(w.geom, t.geom, {M(Trailheads.BesideM)})
                    order by k.metres desc
                    limit 1);
            """);

        // Named by the nearest town or village, where OS Open Names were imported (T064).
        await Execute($"""
            do $do$ begin
              if to_regclass('places.names') is not null then
                update {schema}.trail_networks g
                   set near = (select p.name from places.names p
                                where p.kind in ('City', 'Town', 'Village')
                                order by p.geom <-> g.at limit 1);
              end if;
            end $do$;
            analyze {schema}.trail_networks;
            """);

        await using var counted = new NpgsqlCommand($"select count(*), coalesce(max(metres), 0) from {schema}.trail_networks", connection, transaction);
        await using var reader = await counted.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new TrailRideHotspotBuild(reader.GetInt64(0), reader.GetDouble(1));

        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
