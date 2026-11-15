using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>A stand-in for where the last Request's settings are kept (T056).</summary>
internal sealed class FakeLastRequest(RequestSettings? saved = null) : ILastRequest
{
    /// <summary>The settings kept, as the planner last saved them.</summary>
    public RequestSettings? Saved { get; private set; } = saved;

    public Task<RequestSettings?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);

    public Task SaveAsync(RequestSettings settings, CancellationToken cancellationToken = default)
    {
        Saved = settings;
        return Task.CompletedTask;
    }
}
