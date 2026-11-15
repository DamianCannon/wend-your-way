using WendYourWay.Core.Network;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>A stand-in for the user's Marks (T082), kept in memory.</summary>
internal sealed class FakeMarks : IMarks
{
    private int _next = 1;

    /// <summary>Every Mark kept, oldest first.</summary>
    public List<Mark> Kept { get; } = [];

    public Task<int> BlockAsync(SegmentKey segment, CancellationToken cancellationToken = default)
    {
        if (Kept.FirstOrDefault(m => m.Segment == segment) is { } already) return Task.FromResult(already.Id);
        Kept.Add(new Mark(_next, segment, [new LatLon(51.56, -0.16), new LatLon(51.561, -0.161)]));
        return Task.FromResult(_next++);
    }

    public Task<IReadOnlyList<Mark>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Mark>>([.. Kept]);

    public Task RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        Kept.RemoveAll(m => m.Id == id);
        return Task.CompletedTask;
    }
}
