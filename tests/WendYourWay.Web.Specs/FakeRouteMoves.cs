using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>Saved Routes moved onto the map as it is (T136), as a spec sets it: moved in the Saved Routes given, or kept.</summary>
internal sealed class FakeRouteMoves(FakeSavedRoutes saved) : ISavedRouteMoves
{
    /// <summary>Set, what a move says: kept, and why. Unset, it's moved.</summary>
    public RouteMove? Kept { get; set; }

    public List<int> Asked { get; } = [];

    public Task<RouteMove> MoveAsync(int id, CancellationToken cancellationToken = default)
    {
        Asked.Add(id);
        if (Kept is { } kept) return Task.FromResult(kept);
        saved.MapChanged.Remove(id);
        return Task.FromResult(new RouteMove(RouteMoveOutcome.Moved));
    }
}
