using WendYourWay.Data;

namespace WendYourWay.Web.Specs;

/// <summary>The Network's map data date (T117), set by a spec; none by default.</summary>
internal sealed class FakeNetworkDate : INetworkDate
{
    public DateTimeOffset? At { get; set; }

    public Task<DateTimeOffset?> OsmDataAtAsync(CancellationToken cancellationToken = default) => Task.FromResult(At);
}
