using Npgsql;

namespace WendYourWay.Data;

/// <summary>
/// The map services' API keys kept in the database (T169, #173), by the configuration setting each one
/// stands for: saved, changed and deleted from the Admin panel. Never backed up (UserDataBackups).
/// </summary>
public sealed class ApiKeys(NpgsqlDataSource dataSource)
{
    /// <summary>Every saved key, by name.</summary>
    public async Task<IReadOnlyDictionary<string, string>> AllAsync(CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("select name, value from app.api_keys");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken)) keys[reader.GetString(0)] = reader.GetString(1);
        return keys;
    }

    /// <summary>Saves the key of this name, or changes it: trimmed, since a pasted key carries spaces.</summary>
    public async Task SaveAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await using var command = dataSource.CreateCommand(
            "insert into app.api_keys (name, value) values (@name, @value) on conflict (name) do update set value = excluded.value, changed_at = now()");
        command.Parameters.Add(new NpgsqlParameter("name", name));
        command.Parameters.Add(new NpgsqlParameter("value", value.Trim()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Deletes the key of this name, if it was saved.</summary>
    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("delete from app.api_keys where name = @name");
        command.Parameters.Add(new NpgsqlParameter("name", name));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
