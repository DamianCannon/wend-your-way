using WendYourWay.Core.Network;
using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>What finding the Gravel Off-road networks did (T072): how many were kept, and the largest's metres.</summary>
public sealed record GravelNetworkBuild(long Kept, double LargestM);

/// <summary>
/// The build's Gravel Off-road networks phase (PLAN T072): Off-road ground a bike may use, joined
/// by <see cref="OffRoadJoining"/> through short roads and Traffic-free ground a bike may use, as a
/// Run's Off-road networks are; those of <see cref="KeepFromM"/> or more kept, for Gravel hotspots.
/// </summary>
public static class GravelNetworksPhase
{
    /// <summary>The least a network kept may hold: below a hotspot's 50 km, so that line can move without a build.</summary>
    public const double KeepFromM = 25_000;

    /// <summary>
    /// Finds the Gravel Off-road networks of the Network in <paramref name="schema"/> (network_next
    /// in a build, network on its own) in place of any found before, within <paramref name="transaction"/>.
    /// </summary>
    public static async Task<GravelNetworkBuild> Build(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken cancellationToken, double keepFromM = KeepFromM)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (schema is not ("network" or "network_next")) throw new ArgumentOutOfRangeException(nameof(schema), schema, "Only network or network_next.");

        // Off-road ground first, then what might join it: the order OffRoadJoining needs.
        var joining = new OffRoadJoining();
        var ends = new List<(int Id, long From, long To, float X0, float Y0, float X1, float Y1, float M)>();
        await using (var command = new NpgsqlCommand($"""
            select from_node_id, to_node_id, length_m,
                   ST_X(ST_StartPoint(geom)), ST_Y(ST_StartPoint(geom)), ST_X(ST_EndPoint(geom)), ST_Y(ST_EndPoint(geom)), id
              from {schema}.segments
             where ground = 'off_road' and access_ride <> 'forbidden'
            """, connection, transaction) { CommandTimeout = 0 })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                joining.AddGreen(reader.GetInt64(0), reader.GetInt64(1), reader.GetDouble(2));
                ends.Add((reader.GetInt32(7), reader.GetInt64(0), reader.GetInt64(1), (float)reader.GetDouble(3), (float)reader.GetDouble(4),
                    (float)reader.GetDouble(5), (float)reader.GetDouble(6), (float)reader.GetDouble(2)));
            }
        }
        // Every road a bike may use: one of up to 100 m bridges, as for a Run; any but a Main Road
        // may be part of a road link (T074).
        var links = new List<(long A, long B, double M)>();
        await using (var command = new NpgsqlCommand($"""
            select from_node_id, to_node_id, length_m, main_road
              from {schema}.segments
             where ground <> 'off_road' and access_ride <> 'forbidden'
            """, connection, transaction) { CommandTimeout = 0 })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var (from, to, metres) = (reader.GetInt64(0), reader.GetInt64(1), reader.GetDouble(2));
                joining.AddRoad(from, to, metres);
                if (!reader.GetBoolean(3)) links.Add((from, to, metres));
            }
        }
        var bridged = joining.Finish();

        // Then joined through road links of up to RoadLinking.LinkM (T074): each Segment's network
        // the joined one, and each network's metres its Off-road ground's alone.
        var networkAt = new Dictionary<long, int>();
        for (var i = 0; i < ends.Count; i++)
        {
            networkAt[ends[i].From] = bridged.NetworkOf[i];
            networkAt[ends[i].To] = bridged.NetworkOf[i];
        }
        var linked = RoadLinking.Join(bridged.Metres.Length, networkAt, links);
        links.Clear();
        var metresOf = new double[bridged.Metres.Length];
        for (var n = 0; n < metresOf.Length; n++) metresOf[linked[n]] += bridged.Metres[n];
        var joined = new OffRoadJoining.Joined([.. bridged.NetworkOf.Select(n => linked[n])], metresOf);

        // Each kept network's middle by length (Segments at their midpoints) and box.
        bool Kept(int n) => joined.Metres[n] >= keepFromM;
        var count = joined.Metres.Length;
        var (sx, sy) = (new double[count], new double[count]);
        var (minX, minY, maxX, maxY) = (new double[count], new double[count], new double[count], new double[count]);
        Array.Fill(minX, double.MaxValue);
        Array.Fill(minY, double.MaxValue);
        Array.Fill(maxX, double.MinValue);
        Array.Fill(maxY, double.MinValue);
        for (var i = 0; i < ends.Count; i++)
        {
            var (n, e) = (joined.NetworkOf[i], ends[i]);
            if (!Kept(n)) continue;
            sx[n] += (e.X0 + e.X1) / 2.0 * e.M;
            sy[n] += (e.Y0 + e.Y1) / 2.0 * e.M;
            minX[n] = Math.Min(minX[n], Math.Min(e.X0, e.X1));
            minY[n] = Math.Min(minY[n], Math.Min(e.Y0, e.Y1));
            maxX[n] = Math.Max(maxX[n], Math.Max(e.X0, e.X1));
            maxY[n] = Math.Max(maxY[n], Math.Max(e.Y0, e.Y1));
        }

        // Marked at its own junction nearest that middle, which may lie off its ground.
        var at = new (double X, double Y, double Off)[count];
        Array.Fill(at, (0, 0, double.MaxValue));
        for (var i = 0; i < ends.Count; i++)
        {
            var (n, e) = (joined.NetworkOf[i], ends[i]);
            if (!Kept(n)) continue;
            var (mx, my) = (sx[n] / joined.Metres[n], sy[n] / joined.Metres[n]);
            foreach (var (x, y) in new[] { (e.X0, e.Y0), (e.X1, e.Y1) })
            {
                var off = ((x - mx) * (x - mx)) + ((y - my) * (y - my));
                if (off < at[n].Off) at[n] = (x, y, off);
            }
        }

        await Execute($"truncate {schema}.gravel_networks, {schema}.segment_gravel restart identity");
        var idOf = new Dictionary<int, int>();
        // Largest first, so the ids say their order: a few hundred for Great Britain.
        foreach (var n in Enumerable.Range(0, count).Where(Kept).OrderByDescending(n => joined.Metres[n]))
        {
            await using var insert = new NpgsqlCommand($"""
                insert into {schema}.gravel_networks (metres, at, box)
                values (@metres, ST_SetSRID(ST_MakePoint(@x, @y), 27700), ST_MakeEnvelope(@west, @south, @east, @north, 27700))
                returning id
                """, connection, transaction);
            insert.Parameters.Add(new NpgsqlParameter("metres", (float)joined.Metres[n]));
            insert.Parameters.Add(new NpgsqlParameter("x", at[n].X));
            insert.Parameters.Add(new NpgsqlParameter("y", at[n].Y));
            insert.Parameters.Add(new NpgsqlParameter("west", minX[n]));
            insert.Parameters.Add(new NpgsqlParameter("south", minY[n]));
            insert.Parameters.Add(new NpgsqlParameter("east", maxX[n]));
            insert.Parameters.Add(new NpgsqlParameter("north", maxY[n]));
            idOf[n] = (int)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }

        // Each kept network's Segments (T073), for the car parks beside them.
        await using (var writer = await connection.BeginBinaryImportAsync(
            $"copy {schema}.segment_gravel (segment_id, network_id) from stdin (format binary)", cancellationToken))
        {
            for (var i = 0; i < ends.Count; i++)
            {
                if (!idOf.TryGetValue(joined.NetworkOf[i], out var network)) continue;
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(ends[i].Id, NpgsqlTypes.NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(network, NpgsqlTypes.NpgsqlDbType.Integer, cancellationToken);
            }
            await writer.CompleteAsync(cancellationToken);
        }
        await Execute($"analyze {schema}.segment_gravel");

        // A Gravel Ride's car parks (T073): each Trailhead's largest kept network with a Segment
        // within 100 m, or none. After the Trailheads phase, whose rows these are.
        await Execute($"""
            update {schema}.trailheads t
               set (gravel_network_id, gravel_network_m) = (
                   select g.id, g.metres
                     from {schema}.segments s
                     join {schema}.segment_gravel sg on sg.segment_id = s.id
                     join {schema}.gravel_networks g on g.id = sg.network_id
                    where ST_DWithin(s.geom, t.geom, {Trailheads.BesideM.ToString(System.Globalization.CultureInfo.InvariantCulture)})
                    order by g.metres desc
                    limit 1)
            """);

        // Named by the nearest town or village, where OS Open Names were imported (T064).
        await Execute($"""
            do $do$ begin
              if to_regclass('places.names') is not null then
                update {schema}.gravel_networks g
                   set near = (select p.name from places.names p
                                where p.kind in ('City', 'Town', 'Village')
                                order by p.geom <-> g.at limit 1);
              end if;
            end $do$
            """);
        await Execute($"analyze {schema}.gravel_networks");
        var kept = Enumerable.Range(0, count).Where(Kept).ToArray();
        return new GravelNetworkBuild(kept.Length, kept.Length == 0 ? 0 : kept.Max(n => joined.Metres[n]));

        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
