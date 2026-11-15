using WendYourWay.Data;
using WendYourWay.Data.Building;
using Npgsql;

namespace WendYourWay.Web.Planning;

/// <summary>
/// The map's update from the app (T128): its state read from app.map_updates; Geofabrik asked at
/// most hourly for its map's date, nothing said if it can't be; and `wend update` started as a
/// process of its own by <see cref="WendLauncher"/>.
/// </summary>
/// <param name="source">Where the extract comes from (`MapUpdate:Source`), Geofabrik's Great Britain by default.</param>
/// <param name="dataFolder">Where it's kept (`MapUpdate:Data`), `data` under Wend Your Way's folder by default.</param>
public sealed class ProcessMapUpdater(
    NpgsqlDataSource dataSource, string connectionString, string contentRoot, TimeProvider time, ILogger<ProcessMapUpdater> logger,
    string? source = null, string? dataFolder = null) : IMapUpdater
{
    private readonly string _source = source ?? ExtractSource.GreatBritain;

    private static readonly TimeSpan AskEvery = TimeSpan.FromHours(1);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10), DefaultRequestHeaders = { { "User-Agent", "WendYourWay-map-update/1.0" } } };

    private readonly Lock _gate = new();
    private (DateTimeOffset At, DateTimeOffset? SourceAt)? _asked;

    public async Task<MapUpdateState> StateAsync(CancellationToken cancellationToken = default)
    {
        var updates = new Jobs(dataSource);
        var last = await updates.LastAsync(JobKind.Update, cancellationToken);
        var mapAt = Latest(await updates.NewestSourceAtAsync(cancellationToken), await new PostgisNetworkDate(dataSource).OsmDataAtAsync(cancellationToken));
        return MapUpdateState.Of(last, await SourceAtAsync(cancellationToken), mapAt, time.GetUtcNow(), await updates.LastAsync(cancellationToken: cancellationToken));
    }

    // Geofabrik's date, asked at most hourly; none if it can't be asked.
    private async Task<DateTimeOffset?> SourceAtAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_asked is { } asked && now - asked.At < AskEvery) return asked.SourceAt;
        }
        DateTimeOffset? sourceAt;
        try
        {
            sourceAt = await new ExtractSource(Http, _source).DataAtAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException)
        {
            Log.MapUpdateUnasked(logger, e);
            sourceAt = null;
        }
        lock (_gate) _asked = (now, sourceAt);
        return sourceAt;
    }

    public async Task<string?> StartAsync(CancellationToken cancellationToken = default)
    {
        var state = await StateAsync(cancellationToken);
        if (state.Kind == MapUpdateKind.Running) return "An update is already running.";
        if (state.Busy) return "Something's already running: try again when it's done.";
        var root = WendLauncher.RepositoryRoot(contentRoot) ?? contentRoot;
        return new WendLauncher(contentRoot, connectionString, time).Start(
            "update", ["update", "--source", _source, "--data", dataFolder ?? Path.Combine(root, "data")]);
    }

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
