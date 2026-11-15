using Npgsql;

namespace WendYourWay.Data.Building;

/// <summary>The kinds of job started from the app (T129), as app.map_updates keeps them.</summary>
public static class JobKind
{
    /// <summary>`wend update`: the map brought up to date (T127).</summary>
    public const string Update = "update";

    /// <summary>`wend outings import-downloads`: runs brought in (T130).</summary>
    public const string Import = "import";

    /// <summary>`wend network back`: the map before (T131).</summary>
    public const string Back = "back";

    /// <summary>`wend restore` (T132).</summary>
    public const string Restore = "restore";
}

/// <summary>A job (T127, T129), as app.map_updates keeps it.</summary>
/// <param name="Kind">What it does: one of <see cref="JobKind"/>'s.</param>
/// <param name="Stage">How far it got: for an update, checking, downloading, building, built, calibrating, backing up.</param>
/// <param name="Outcome">running, finished, up to date or failed.</param>
/// <param name="Message">A sentence saying how it ended, or why it failed.</param>
/// <param name="Source">Where it took from: an update's extract, an import's folder.</param>
/// <param name="SourceAt">For an update, when the source said its data runs to, once asked.</param>
public sealed record Job(
    int Id, DateTimeOffset StartedAt, DateTimeOffset HeartbeatAt, DateTimeOffset? FinishedAt,
    string Stage, string Outcome, string? Message, string Source, DateTimeOffset? SourceAt, string Kind = JobKind.Update)
{
    /// <summary>A run that hasn't said it's alive for this long has stopped.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromMinutes(5);

    /// <summary>Whether it says it's running but has stopped: killed, or the laptop turned off.</summary>
    public bool Stopped(DateTimeOffset now) => Outcome == "running" && now - HeartbeatAt > Silence;

    /// <summary>Whether its map was swapped in but what follows (scores, stars, backup) didn't finish.</summary>
    public bool BuiltButNotFinished => Stage is "built" or "moving Saved Routes" or "calibrating" or "backing up" && Outcome != "finished";
}

/// <summary>
/// The jobs started from the app or by `wend` (T127, T129), in app.map_updates (named for its first):
/// written as they go, read by the next run and the app. Not the user's data, so not backed up.
/// </summary>
public sealed class Jobs(NpgsqlDataSource dataSource)
{
    /// <summary>A run of <paramref name="kind"/> begun, from <paramref name="source"/>; its id.</summary>
    public async Task<int> StartAsync(string kind, string source, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("insert into app.map_updates (kind, stage, source) values (@kind, 'starting', @source) returning id");
        command.Parameters.Add(new NpgsqlParameter("kind", kind));
        command.Parameters.Add(new NpgsqlParameter("source", source));
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>How far it has got, and when the source's data runs to once known.</summary>
    public async Task StageAsync(int id, string stage, DateTimeOffset? sourceAt = null, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "update app.map_updates set stage = @stage, source_at = coalesce(@sourceAt, source_at), heartbeat_at = now() where id = @id");
        command.Parameters.Add(new NpgsqlParameter("stage", stage));
        command.Parameters.Add(new NpgsqlParameter("sourceAt", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)sourceAt ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Still alive.</summary>
    public async Task HeartbeatAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("update app.map_updates set heartbeat_at = now() where id = @id");
        command.Parameters.Add(new NpgsqlParameter("id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Ended: <paramref name="outcome"/> (finished, up to date or failed), and why.</summary>
    public async Task EndAsync(int id, string outcome, string message, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "update app.map_updates set outcome = @outcome, message = @message, finished_at = now(), heartbeat_at = now() where id = @id");
        command.Parameters.Add(new NpgsqlParameter("outcome", outcome));
        command.Parameters.Add(new NpgsqlParameter("message", message));
        command.Parameters.Add(new NpgsqlParameter("id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The last run of <paramref name="kind"/>, or of any kind, if any.</summary>
    public async Task<Job?> LastAsync(string? kind = null, CancellationToken cancellationToken = default) =>
        First(await ReadAsync("where (@kind::text is null or kind = @kind) order by id desc limit 1", null, kind, cancellationToken));

    /// <summary>The last run of <paramref name="kind"/> that ended well (finished, or up to date), if any (T138).</summary>
    public async Task<Job?> LastEndedWellAsync(string kind, CancellationToken cancellationToken = default) =>
        First(await ReadAsync("where kind = @kind and outcome in ('finished', 'up to date') and finished_at is not null order by id desc limit 1", null, kind, cancellationToken));

    /// <summary>The last update before <paramref name="before"/>, if any.</summary>
    public async Task<Job?> LastUpdateBeforeAsync(int before, CancellationToken cancellationToken = default) =>
        First(await ReadAsync("where id < @before and kind = @kind order by id desc limit 1", before, JobKind.Update, cancellationToken));

    /// <summary>The last job of these kinds before <paramref name="before"/>, if any (T131).</summary>
    public async Task<Job?> LastBeforeAsync(int before, IReadOnlyList<string> kinds, CancellationToken cancellationToken = default) =>
        First(await ReadAsync("where id < @before and kind = any(@kinds) order by id desc limit 1", before, null, cancellationToken, kinds));

    private static Job? First(IReadOnlyList<Job> runs) => runs.Count > 0 ? runs[0] : null;

    /// <summary>When the data of the newest map an update brought in, or found up to date, runs to.</summary>
    public async Task<DateTimeOffset?> NewestSourceAtAsync(CancellationToken cancellationToken = default)
    {
        await using var query = dataSource.CreateCommand(
            """
            select max(source_at) from app.map_updates u
             where kind = 'update' and outcome in ('finished', 'up to date')
               -- Gone back since (T131), the map is no longer that update's.
               and id > coalesce((select max(id) from app.map_updates where kind = 'back' and outcome = 'finished'), 0)
            """);
        return await query.ExecuteScalarAsync(cancellationToken) is DateTime at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;
    }

    private async Task<IReadOnlyList<Job>> ReadAsync(string rest, int? before, string? kind, CancellationToken cancellationToken, IReadOnlyList<string>? kinds = null)
    {
        await using var query = dataSource.CreateCommand(
            "select id, started_at, heartbeat_at, finished_at, stage, outcome, message, source, source_at, kind from app.map_updates " + rest);
        if (before is { } id) query.Parameters.Add(new NpgsqlParameter("before", id));
        query.Parameters.Add(new NpgsqlParameter("kind", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)kind ?? DBNull.Value });
        if (kinds is not null) query.Parameters.Add(new NpgsqlParameter("kinds", kinds.ToArray()));
        var runs = new List<Job>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(new Job(reader.Get<int>("id"), reader.Get<DateTimeOffset>("started_at"), reader.Get<DateTimeOffset>("heartbeat_at"),
                reader.Maybe<DateTimeOffset>("finished_at"), reader.Get<string>("stage"), reader.Get<string>("outcome"), reader.MaybeText("message"),
                reader.Get<string>("source"), reader.Maybe<DateTimeOffset>("source_at"), reader.Get<string>("kind")));
        }
        return runs;
    }
}
