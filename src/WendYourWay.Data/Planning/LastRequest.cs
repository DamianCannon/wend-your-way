using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace WendYourWay.Data.Planning;

/// <summary>A Request's settings, restored when the planner opens (R1.1a, T056).</summary>
/// <param name="Start">Where it started, or null for home: home's position is never kept here.</param>
/// <param name="DistanceKm">Its Distance.</param>
/// <param name="HillsLean">Its Hills Lean, −1 to +1.</param>
/// <param name="OffRoadLean">Its Off-road Lean, −1 to +1 (T058): 0 when kept before there was one.</param>
/// <param name="Activity">A Run or a Gravel Ride (T068): a Run when kept before there was a choice.</param>
public sealed record RequestSettings(LatLon? Start, double DistanceKm, double HillsLean, double OffRoadLean = 0, WendYourWay.Core.Loops.Activity Activity = WendYourWay.Core.Loops.Activity.Run)
{
    /// <summary>Where a Point-to-point ends (T091), or null for a Loop back to the start.</summary>
    public LatLon? Finish { get; init; }

    /// <summary>A Point-to-point's Distance (T091), or null for a spread from shortest to greenest.</summary>
    public double? FinishDistanceKm { get; init; }

    /// <summary>What the finish is called (T093): a place's name or a postcode; null if neither.</summary>
    public string? FinishName { get; init; }

    /// <summary>Whether Explain? was on (T157): the route's notes on the map. Off when kept before there was one.</summary>
    public bool Explain { get; init; }
}

/// <summary>Keeps the last Request's settings.</summary>
public interface ILastRequest
{
    /// <summary>The last Request's settings, or null before any Request.</summary>
    Task<RequestSettings?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Keeps these as the last Request's settings, in place of any before.</summary>
    Task SaveAsync(RequestSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>The last Request's settings in app.settings.last_request (jsonb, ARCHITECTURE).</summary>
public sealed class PostgisLastRequest(NpgsqlDataSource dataSource) : ILastRequest
{
    private static JsonSerializerOptions Json => AppJson.Options;

    public async Task<RequestSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand("select last_request::text from app.settings");
        return await query.ExecuteScalarAsync(cancellationToken) is string json ? JsonSerializer.Deserialize<RequestSettings>(json, Json) : null;
    }

    public async Task SaveAsync(RequestSettings settings, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            insert into app.settings (last_request) values (@last)
            on conflict (id) do update set last_request = excluded.last_request
            """);
        command.Parameters.Add(new NpgsqlParameter("last", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(settings, Json) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
