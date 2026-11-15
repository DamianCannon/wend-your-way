using WendYourWay.Core.Network;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>A stand-in for a route's turn points (T100): these, for whatever route is asked about.</summary>
internal sealed class FakeRouteTurns : IRouteTurns
{
    public IReadOnlyList<RouteTurn> Turns { get; set; } = [];

    /// <summary>Each route asked about, by its Segments.</summary>
    public List<IReadOnlyList<(SegmentKey Key, bool Forward)>> Asked { get; } = [];

    /// <summary>Set, the turns are held till it's done: an export that takes a while (T139).</summary>
    public TaskCompletionSource? Hold { get; set; }

    public async Task<IReadOnlyList<RouteTurn>> OfAsync(IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, CancellationToken cancellationToken = default)
    {
        Asked.Add(traversals);
        if (Hold is { } hold) await hold.Task;
        return Turns;
    }
}
