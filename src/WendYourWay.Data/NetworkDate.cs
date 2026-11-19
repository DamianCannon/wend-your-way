using Npgsql;

namespace WendYourWay.Data;

/// <summary>
/// How recent the Network's map data is (PLAN T117): the date of the OpenStreetMap extract it was
/// built from, so a path newer than that, drawn on the map's tiles but not in the Network, explains
/// itself.
/// </summary>
public interface INetworkDate
{
    /// <summary>The extract's date, or none for a Network built before it was kept.</summary>
    Task<DateTimeOffset?> OsmDataAtAsync(CancellationToken cancellationToken = default);
}

/// <summary>The Network's date from network.built_from, written by the build.</summary>
public sealed class PostgisNetworkDate(NpgsqlDataSource dataSource) : INetworkDate
{
    public async Task<DateTimeOffset?> OsmDataAtAsync(CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand("select osm_data_at from network.built_from");
        return await query.ExecuteScalarAsync(cancellationToken) is DateTime at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;
    }

    /// <summary>When the map in use was built (T150): a rebuild from the same extract keeps its date, not this.</summary>
    public async Task<DateTimeOffset?> BuiltAtAsync(CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand("select built_at from network.built_from");
        return await query.ExecuteScalarAsync(cancellationToken) is DateTime at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;
    }
}
