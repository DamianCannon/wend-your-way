using Npgsql;

namespace WendYourWay.Data.Outings;

/// <summary>
/// The user's Outings at a glance (T129): how many, how many they haven't rated, and when the
/// last came in (none known for those imported before it was kept).
/// </summary>
public sealed record OutingCounts(int Outings, int Unrated, DateTimeOffset? LastImported)
{
    public static async Task<OutingCounts> CountAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var query = dataSource.CreateCommand(
            "select count(*) as outings, count(*) filter (where user_stars is null) as unrated, max(imported_at) as last_imported from app.outings");
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new OutingCounts((int)reader.Get<long>("outings"), (int)reader.Get<long>("unrated"), reader.Maybe<DateTimeOffset>("last_imported"));
    }
}
