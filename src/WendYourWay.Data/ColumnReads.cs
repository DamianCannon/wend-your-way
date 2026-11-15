using Npgsql;

namespace WendYourWay.Data;

/// <summary>
/// A row's columns read by name, not by position (T113), so a column added or moved in a query
/// can't shift another one's value.
/// </summary>
internal static class ColumnReads
{
    /// <summary>The column <paramref name="name"/>, which must not be null.</summary>
    public static T Get<T>(this NpgsqlDataReader reader, string name) => reader.GetFieldValue<T>(reader.GetOrdinal(name));

    /// <summary>The column <paramref name="name"/>, or none where it is null.</summary>
    public static T? Maybe<T>(this NpgsqlDataReader reader, string name) where T : struct
    {
        var at = reader.GetOrdinal(name);
        return reader.IsDBNull(at) ? null : reader.GetFieldValue<T>(at);
    }

    /// <summary>The text column <paramref name="name"/>, or none where it is null.</summary>
    public static string? MaybeText(this NpgsqlDataReader reader, string name)
    {
        var at = reader.GetOrdinal(name);
        return reader.IsDBNull(at) ? null : reader.GetString(at);
    }
}
