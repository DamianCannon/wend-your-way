using WendYourWay.Core.Network;
using Npgsql;
using NpgsqlTypes;

namespace WendYourWay.Data.Building;

/// <summary>What finding the Off-road networks did (T022).</summary>
/// <param name="Networks">How many it found.</param>
/// <param name="GreenSegments">The green Segments in them.</param>
/// <param name="LargestM">The largest network's metres of green ground.</param>
public sealed record OffRoadBuild(long Networks, long GreenSegments, double LargestM);

/// <summary>
/// The build's Off-road networks phase (PLAN T022): every green Segment's network and each network's
/// size and extent, found by <see cref="OffRoadJoining"/> and written by COPY.
/// </summary>
public static class OffRoadNetworksPhase
{
    /// <summary>
    /// Finds the Off-road networks of the Network in <paramref name="schema"/> (network_next in a
    /// build, network on its own) in place of any found before, within <paramref name="transaction"/>.
    /// </summary>
    public static async Task<OffRoadBuild> Build(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (schema is not ("network" or "network_next")) throw new ArgumentOutOfRangeException(nameof(schema), schema, "Only network or network_next.");

        // Green Segments first, then the roads that might join them: the order OffRoadJoining needs.
        var joining = new OffRoadJoining();
        var ids = new List<int>();
        var ends = new List<(float X0, float Y0, float X1, float Y1)>();
        await using (var command = new NpgsqlCommand($"""
            select id, from_node_id, to_node_id, length_m,
                   ST_X(ST_StartPoint(geom)), ST_Y(ST_StartPoint(geom)), ST_X(ST_EndPoint(geom)), ST_Y(ST_EndPoint(geom))
              from {schema}.segments
             where ground in ('off_road', 'traffic_free') and access_run <> 'forbidden'
               -- A possible gap (T146) is used only when the user includes it: it joins nothing.
               and gap is distinct from 'possible'
            """, connection, transaction) { CommandTimeout = 0 })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                joining.AddGreen(reader.GetInt64(1), reader.GetInt64(2), reader.GetDouble(3));
                ids.Add(reader.GetInt32(0));
                ends.Add(((float)reader.GetDouble(4), (float)reader.GetDouble(5), (float)reader.GetDouble(6), (float)reader.GetDouble(7)));
            }
        }
        await using (var command = new NpgsqlCommand($"""
            select from_node_id, to_node_id, length_m
              from {schema}.segments
             where ground = 'connector' and access_run <> 'forbidden' and length_m <= @longest
            """, connection, transaction) { CommandTimeout = 0 })
        {
            command.Parameters.Add(new NpgsqlParameter("longest", OffRoadNetworks.BridgedRoadM));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) joining.AddRoad(reader.GetInt64(0), reader.GetInt64(1), reader.GetDouble(2));
        }
        var joined = joining.Finish();

        // Each network's Segments and the box round their ends.
        var count = new int[joined.Metres.Length];
        var (minX, minY, maxX, maxY) = (new float[count.Length], new float[count.Length], new float[count.Length], new float[count.Length]);
        Array.Fill(minX, float.MaxValue);
        Array.Fill(minY, float.MaxValue);
        Array.Fill(maxX, float.MinValue);
        Array.Fill(maxY, float.MinValue);
        for (var i = 0; i < ids.Count; i++)
        {
            var (n, e) = (joined.NetworkOf[i], ends[i]);
            count[n]++;
            minX[n] = Math.Min(minX[n], Math.Min(e.X0, e.X1));
            minY[n] = Math.Min(minY[n], Math.Min(e.Y0, e.Y1));
            maxX[n] = Math.Max(maxX[n], Math.Max(e.X0, e.X1));
            maxY[n] = Math.Max(maxY[n], Math.Max(e.Y0, e.Y1));
        }

        await Execute($"truncate {schema}.segment_offroad, {schema}.offroad_networks");
        await using (var writer = await connection.BeginBinaryImportAsync($"copy {schema}.segment_offroad (segment_id, network_id) from stdin (format binary)", cancellationToken))
        {
            for (var i = 0; i < ids.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(ids[i], NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(joined.NetworkOf[i], NpgsqlDbType.Integer, cancellationToken);
            }
            await writer.CompleteAsync(cancellationToken);
        }

        // Boxes made Polygons by PostGIS, widened by a metre so a straight network has an area.
        await Execute("create temp table tmp_offroad (id integer, metres real, segments integer, x0 real, y0 real, x1 real, y1 real) on commit drop");
        await using (var writer = await connection.BeginBinaryImportAsync("copy tmp_offroad (id, metres, segments, x0, y0, x1, y1) from stdin (format binary)", cancellationToken))
        {
            for (var n = 0; n < count.Length; n++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(n, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync((float)joined.Metres[n], NpgsqlDbType.Real, cancellationToken);
                await writer.WriteAsync(count[n], NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(minX[n], NpgsqlDbType.Real, cancellationToken);
                await writer.WriteAsync(minY[n], NpgsqlDbType.Real, cancellationToken);
                await writer.WriteAsync(maxX[n], NpgsqlDbType.Real, cancellationToken);
                await writer.WriteAsync(maxY[n], NpgsqlDbType.Real, cancellationToken);
            }
            await writer.CompleteAsync(cancellationToken);
        }
        await Execute($"""
            insert into {schema}.offroad_networks (id, metres, segments, extent)
            select id, metres, segments, ST_MakeEnvelope(x0 - 1, y0 - 1, x1 + 1, y1 + 1, 27700) from tmp_offroad;
            drop table tmp_offroad
            """);
        return new OffRoadBuild(count.Length, ids.Count, joined.Metres.Length == 0 ? 0 : joined.Metres.Max());

        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Finds the Off-road networks of the Network the app reads and their Trailheads (T023), on
    /// their own, in one transaction.
    /// </summary>
    public static async Task<(OffRoadBuild OffRoad, TrailheadBuild Trailheads, ObstacleBuild Obstacles, GravelNetworkBuild Gravel, TrailRideHotspotBuild TrailRide, RunHotspotBuild Run)> Rebuild(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var built = await Build(connection, transaction, "network", cancellationToken);
        await using (var command = new NpgsqlCommand("analyze network.offroad_networks; analyze network.segment_offroad", connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var trailheads = await TrailheadsPhase.Build(connection, transaction, "network", cancellationToken);
        // Obstacles too (T069): a rule for them shouldn't need the whole Network rebuilt.
        var obstacles = await ObstaclesPhase.Build(connection, transaction, "network", cancellationToken);
        // And the Gravel Off-road networks (T072), for the Gravel hotspots.
        var gravel = await GravelNetworksPhase.Build(connection, transaction, "network", cancellationToken);
        // And the Trail Ride hotspots (T189).
        var trailRide = await TrailRideHotspotsPhase.Build(connection, transaction, "network", cancellationToken);
        // And the squares Run hotspots are found in (T190).
        var run = await RunHotspotsPhase.Build(connection, transaction, "network", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (built, trailheads, obstacles, gravel, trailRide, run);
    }
}
