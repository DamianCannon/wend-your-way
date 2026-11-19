using WendYourWay.Data.Building;

namespace WendYourWay.Web.Planning;

/// <summary>Where the map's update is (T128): never run, running, stopped part-way, or how the last one ended.</summary>
public enum MapUpdateKind
{
    None,
    Running,
    Stopped,
    Finished,
    UpToDate,
    Failed,
}

/// <summary>
/// What the planner says of the map's update (T128): the last run's kind, stage, start and sentence;
/// when Geofabrik's map runs to (none if it couldn't be asked) and the map in use does; and whether
/// a job of another kind is running, holding the one lock (T132); and when the map in use was built (T150).
/// </summary>
public sealed record MapUpdateState(
    MapUpdateKind Kind, string? Stage, DateTimeOffset? StartedAt, string? Message, DateTimeOffset? SourceAt, DateTimeOffset? MapAt, bool Busy = false,
    DateTimeOffset? MapBuiltAt = null)
{
    /// <summary>Before anything's known.</summary>
    public static MapUpdateState Unknown { get; } = new(MapUpdateKind.None, null, null, null, null, null);

    /// <summary>Whether Geofabrik's map is later than the one in use (or the one in use has no date).</summary>
    public bool Newer => SourceAt is { } source && (MapAt is not { } map || source > map);

    /// <summary>From the last run (none if never), Geofabrik's date, the map in use's, the time, and the last job of any kind.</summary>
    public static MapUpdateState Of(Job? last, DateTimeOffset? sourceAt, DateTimeOffset? mapAt, DateTimeOffset now, Job? lastOfAny = null, DateTimeOffset? mapBuiltAt = null) =>
        FromLast(last, sourceAt, mapAt, now) with
        {
            Busy = lastOfAny is { Outcome: "running" } other && other.Kind != JobKind.Update && !other.Stopped(now),
            MapBuiltAt = mapBuiltAt,
        };

    private static MapUpdateState FromLast(Job? last, DateTimeOffset? sourceAt, DateTimeOffset? mapAt, DateTimeOffset now) =>
        last is null
            ? new(MapUpdateKind.None, null, null, null, sourceAt, mapAt)
            : new(last.Outcome switch
            {
                "running" => last.Stopped(now) ? MapUpdateKind.Stopped : MapUpdateKind.Running,
                "finished" => MapUpdateKind.Finished,
                "up to date" => MapUpdateKind.UpToDate,
                _ => MapUpdateKind.Failed,
            }, last.Doing, last.StartedAt, last.Message, sourceAt, mapAt);
}

/// <summary>The map's update, as the planner sees it (T128): what it's doing, and starting one.</summary>
public interface IMapUpdater
{
    Task<MapUpdateState> StateAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts `wend update` in a process of its own; none if it started, else why not.</summary>
    Task<string?> StartAsync(CancellationToken cancellationToken = default);
}
