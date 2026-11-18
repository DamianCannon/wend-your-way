using System.Diagnostics;
using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>What cutting Segments did, so nothing is dropped silently.</summary>
/// <param name="WaysRead">Routable ways osm2pgsql wrote.</param>
/// <param name="WaysMisaligned">Ways skipped because their geometry didn't match their node IDs one-to-one.</param>
/// <param name="SegmentsWritten">Segments in the Network.</param>
/// <param name="IdentityClashes">Segments skipped because another already had the same OSM identity.</param>
/// <param name="ZeroLength">Segments skipped because both ends were at the same spot.</param>
/// <param name="CountriesFound">
/// The countries the extract held whole, as ISO 3166-2 codes ("GB-ENG,GB-SCT,GB-WLS"); empty if
/// none. The Network is clipped to England and Wales only when it held both.
/// </param>
/// <param name="InScotland">Segments left out because they're in Scotland; null when not clipped.</param>
/// <param name="Elsewhere">
/// Segments left out because they're in none of the three countries (the sea, abroad); null when
/// not clipped. More than a few means the boundary is missing some ground.
/// </param>
public sealed record SegmentBuild(
    long WaysRead, long WaysMisaligned, long SegmentsWritten, long IdentityClashes, long ZeroLength,
    string CountriesFound, long? InScotland, long? Elsewhere)
{
    /// <summary>Whether the Network was clipped to England and Wales.</summary>
    public bool Clipped => InScotland is not null;

    /// <summary>Gaps under 5 m joined (#150, T146a): a path's dead end and another way it stops short of.</summary>
    public long GapsJoined { get; init; }

    /// <summary>Gaps of 5 m to 15 m kept as possible, used only when the user includes one.</summary>
    public long GapsPossible { get; init; }
}

/// <summary>What finding same ground did.</summary>
/// <param name="PathsBesideRoads">Path Segments found running mostly beside roads; the paved ones are now Connectors.</param>
public sealed record SameGroundBuild(long PathsBesideRoads);

/// <summary>What crossing Open ground did (PLAN T039).</summary>
/// <param name="Crossings">Open ground Segments made, straight across public open land.</param>
/// <param name="LengthM">Their length together.</param>
public sealed record OpenGroundBuild(long Crossings, double LengthM);

/// <summary>What sampling the ground's height along the Segments did (PLAN T049).</summary>
/// <param name="Segments">Segments given their climbing.</param>
/// <param name="AscentM">Their ascent together, each from its from node to its to node.</param>
public sealed record ClimbingBuild(long Segments, double AscentM);

/// <summary>What one Network build did, phase by phase.</summary>
/// <param name="Climbing">What the climbing phase did, or null when the build had no terrain.</param>
/// <param name="OutingsCovered">The user's Outings whose coverage was worked out again on the new Network.</param>
public sealed record NetworkBuildReport(SegmentBuild Segments, SameGroundBuild SameGround, OpenGroundBuild OpenGround, OffRoadBuild OffRoad, TrailheadBuild Trailheads, ClimbingBuild? Climbing, long OutingsCovered, ObstacleBuild? Obstacles = null, GravelNetworkBuild? GravelNetworks = null, RightsOfWayBuild? RightsOfWay = null);

/// <summary>Builds the Network from the raw OSM ways that <see cref="OsmImport"/> loaded.</summary>
public static class NetworkBuild
{
    /// <summary>
    /// Builds a new Network beside the one the app reads (network_next), from Segments cut from
    /// osm.ways, the same ground between them, crossings of Open ground and, given
    /// <paramref name="terrain"/>, each Segment's climbing (none without it); swaps it in, keeping the old one as network_prev;
    /// and works out again what each of the user's Outings covered on it. All in one transaction:
    /// a build that fails or is cancelled part-way leaves the previous Network whole, and nothing
    /// is deleted in place (PLAN T034).
    /// <paramref name="phaseDone"/> hears about each phase ("segments", "same ground",
    /// "open ground", "off-road networks", "driving", "trailheads", "climbing", "outings") as it finishes, with how long it took.
    /// </summary>
    public static async Task<NetworkBuildReport> Build(
        NpgsqlDataSource dataSource, TerrainGrid? terrain = null, Action<string, TimeSpan>? phaseDone = null, string? rightsOfWay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // More memory for the build's sorts and temp-table indexes than PostgreSQL's defaults
        // (4 MB and 64 MB), within reach of a 16 GB laptop; for this transaction only.
        await using (var command = Command("set local work_mem = '64MB'; set local maintenance_work_mem = '512MB'"))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = Command("select prepare_network_next()"))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var clock = Stopwatch.StartNew();

        // The Definitive Map byways, before the Segments are matched to them (T021). Without a
        // folder, those loaded before are kept.
        RightsOfWayBuild? rights = null;
        if (rightsOfWay is not null)
        {
            rights = await RightsOfWayPhase.Load(connection, transaction, rightsOfWay, cancellationToken);
            phaseDone?.Invoke("rights of way", clock.Elapsed);
            clock.Restart();
        }

        SegmentBuild segments;
        await using (var command = Command("select ways_read, ways_misaligned, segments_written, identity_clashes, zero_length, countries_found, in_scotland, elsewhere, gaps_joined, gaps_possible from build_segments()"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            segments = new SegmentBuild(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetInt64(7))
            {
                GapsJoined = reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
                GapsPossible = reader.IsDBNull(9) ? 0 : reader.GetInt64(9),
            };
        }
        phaseDone?.Invoke("segments", clock.Elapsed);

        clock.Restart();
        SameGroundBuild sameGround;
        await using (var command = Command("select paths_beside_roads from group_same_ground()"))
        {
            sameGround = new SameGroundBuild((long)(await command.ExecuteScalarAsync(cancellationToken))!);
        }
        phaseDone?.Invoke("same ground", clock.Elapsed);

        clock.Restart();
        OpenGroundBuild openGround;
        await using (var command = Command("select crossings, length_m from build_open_ground()"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            openGround = new OpenGroundBuild(reader.GetInt64(0), reader.GetDouble(1));
        }
        phaseDone?.Invoke("open ground", clock.Elapsed);

        // After Open ground, whose crossings are green ground too (T022).
        clock.Restart();
        var offRoad = await OffRoadNetworksPhase.Build(connection, transaction, "network_next", cancellationToken);
        await using (var command = Command("analyze network_next.segment_offroad; analyze network_next.offroad_networks"))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        phaseDone?.Invoke("off-road networks", clock.Elapsed);

        clock.Restart();
        var trailheads = await TrailheadsPhase.Build(connection, transaction, "network_next", cancellationToken);
        phaseDone?.Invoke("trailheads", clock.Elapsed);

        clock.Restart();
        var obstacles = await ObstaclesPhase.Build(connection, transaction, "network_next", cancellationToken);
        phaseDone?.Invoke("obstacles", clock.Elapsed);

        clock.Restart();
        var gravel = await GravelNetworksPhase.Build(connection, transaction, "network_next", cancellationToken);
        phaseDone?.Invoke("gravel networks", clock.Elapsed);

        // After Open ground, so its crossings climb too.
        ClimbingBuild? climbing = null;
        if (terrain is not null)
        {
            clock.Restart();
            climbing = await ClimbingPhase.Build(connection, transaction, terrain, cancellationToken);
            phaseDone?.Invoke("climbing", clock.Elapsed);
        }

        // In order of place, after every phase that writes Segments (an update scatters a row):
        // a slice then reads neighbouring pages, not pages from all over the table (T055).
        clock.Restart();
        await using (var command = Command("cluster network_next.segments using ix_segments_place"))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        phaseDone?.Invoke("in order of place", clock.Elapsed);

        // What it was built from (T117): the extract's own date, as osm2pgsql recorded it.
        await using (var command = Command("""
            insert into network_next.built_from (osm_data_at)
            select case when to_regclass('public.osm2pgsql_properties') is not null then (
                     select coalesce(
                       (select value from public.osm2pgsql_properties where property = 'replication_timestamp'),
                       (select value from public.osm2pgsql_properties where property = 'import_timestamp'))::timestamptz)
                   end
            """))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // The swap: readers see the new Network when this transaction commits, never before.
        await using (var command = Command("alter schema network rename to network_prev; alter schema network_next rename to network"))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Segment identities change when OSM does, so coverage is derived afresh (ADR 0005).
        clock.Restart();
        long outings;
        await using (var command = Command("select cover_outings()"))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        // Scores of the old Network are no score of the new: scored again after this commits (T097).
        await using (var command = Command("update app.outings set score = null, matched_share = null, off_road_share = null, climb_per_km = null"))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var command = Command("select count(*) from app.outings"))
        {
            outings = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        phaseDone?.Invoke("outings", clock.Elapsed);

        // Fresh statistics, so queries on the new Network are planned for it as it is.
        await using (var command = Command("analyze network.segments; analyze network.segment_beside; analyze network.offroad_networks; analyze network.segment_offroad; analyze network.trailheads; analyze network.segment_obstacles"))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new NetworkBuildReport(segments, sameGround, openGround, offRoad, trailheads, climbing, outings, obstacles, gravel, rights);

        // A large extract takes minutes.
        NpgsqlCommand Command(string sql) => new(sql, connection, transaction) { CommandTimeout = 0 };
    }
}
