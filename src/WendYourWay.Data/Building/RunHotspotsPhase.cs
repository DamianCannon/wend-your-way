using System.Globalization;
using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>What counting the off-road ground for Run hotspots did (T190): its squares, and the most one holds.</summary>
public sealed record RunHotspotBuild(long Squares, double DensestM);

/// <summary>
/// The build's Run hotspots phase (T190, #194): the off-road ground a Run may use (the unconfirmed too), counted in
/// squares of <see cref="SquareM"/> on Britain's grid, each by where its ways' middles lie, and marked at its own
/// junction nearest its middle by length. Every square is kept; the app joins the dense ones into Run hotspots as
/// it reads them. On foot, Off-road networks join up across whole counties, so their size says little; the spike
/// (T187) found density does pick out the places: the New Forest, the Lake District, Exmoor.
/// </summary>
public static class RunHotspotsPhase
{
    /// <summary>A square's side.</summary>
    public const double SquareM = 5_000;

    /// <summary>
    /// Counts the squares of the Network in <paramref name="schema"/> (network_next in a build, network on its own) in
    /// place of any counted before, within <paramref name="transaction"/>.
    /// </summary>
    public static async Task<RunHotspotBuild> Build(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema,
        CancellationToken cancellationToken, double squareM = SquareM)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (schema is not ("network" or "network_next")) throw new ArgumentOutOfRangeException(nameof(schema), schema, "Only network or network_next.");
        var size = squareM.ToString(CultureInfo.InvariantCulture);

        await Execute($"""
            drop table if exists run_ways;
            create temp table run_ways on commit drop as
            select geom, length_m, middle, floor(ST_X(middle) / {size})::integer as sx, floor(ST_Y(middle) / {size})::integer as sy
              from (select geom, length_m, ST_LineInterpolatePoint(geom, 0.5) as middle
                      from {schema}.segments
                     where ground = 'off_road' and access_run <> 'forbidden') s;
            create index on run_ways (sx, sy);
            analyze run_ways;

            truncate {schema}.run_squares;
            insert into {schema}.run_squares (metres, box, at)
            select q.metres,
                   ST_MakeEnvelope(q.sx * {size}, q.sy * {size}, (q.sx + 1) * {size}, (q.sy + 1) * {size}, 27700),
                   (select ST_StartPoint(w.geom) from run_ways w where w.sx = q.sx and w.sy = q.sy order by w.middle <-> q.middle limit 1)
              from (select sx, sy, sum(length_m) as metres,
                           ST_SetSRID(ST_MakePoint(sum(ST_X(middle) * length_m) / sum(length_m), sum(ST_Y(middle) * length_m) / sum(length_m)), 27700) as middle
                      from run_ways group by sx, sy having sum(length_m) > 0) q;
            analyze {schema}.run_squares;
            """);

        await using var counted = new NpgsqlCommand($"select count(*), coalesce(max(metres), 0) from {schema}.run_squares", connection, transaction);
        await using var reader = await counted.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new RunHotspotBuild(reader.GetInt64(0), reader.GetDouble(1));

        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
