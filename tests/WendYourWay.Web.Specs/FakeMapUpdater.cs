using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>The map's updater (T128), as a spec sets it: what it says, and the updates started.</summary>
internal sealed class FakeMapUpdater : IMapUpdater
{
    public MapUpdateState State { get; set; } = MapUpdateState.Unknown;

    /// <summary>Set, what it says once an update is started.</summary>
    public MapUpdateState? StateAfterStart { get; set; }

    /// <summary>Set, why an update can't be started.</summary>
    public string? Refusal { get; set; }

    public int Started { get; private set; }

    public Task<MapUpdateState> StateAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);

    public Task<string?> StartAsync(CancellationToken cancellationToken = default)
    {
        if (Refusal is not null) return Task.FromResult<string?>(Refusal);
        Started++;
        if (StateAfterStart is { } after) State = after;
        return Task.FromResult<string?>(null);
    }
}
