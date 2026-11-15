using WendYourWay.Core.Network;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>A stand-in for the Saved Routes (T080), kept in memory.</summary>
internal sealed class FakeSavedRoutes : ISavedRoutes
{
    private int _next = 1;

    /// <summary>The app's score each route was saved with (T097).</summary>
    public Dictionary<int, RouteScore?> Scores { get; } = [];

    /// <summary>The user's stars for each route rated (T097).</summary>
    public Dictionary<int, int?> Rated { get; } = [];

    /// <summary>The routes the map has changed under since they were saved (T103).</summary>
    public HashSet<int> MapChanged { get; } = [];

    /// <summary>Every route saved and not deleted, oldest first.</summary>
    public List<(int Id, string Name, PlannedLoop Loop, IReadOnlyList<(SegmentKey Key, bool Forward)> Traversals)> Kept { get; } = [];

    public Task<int> SaveAsync(string name, PlannedLoop planned, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, RouteScore? score = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A Saved Route needs a name.", nameof(name));
        Kept.Add((_next, name.Trim(), planned, traversals));
        Scores[_next] = score;
        return Task.FromResult(_next++);
    }

    /// <summary>Each route moved onto another map (T136), and what it was moved to.</summary>
    public List<(int Id, PlannedLoop Loop)> Moved { get; } = [];

    public Task MoveAsync(int id, PlannedLoop planned, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, RouteScore? score, CancellationToken cancellationToken = default)
    {
        Moved.Add((id, planned));
        var at = Kept.FindIndex(r => r.Id == id);
        if (at >= 0) Kept[at] = (id, Kept[at].Name, planned, traversals);
        Scores[id] = score;
        MapChanged.Remove(id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SavedRouteSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SavedRouteSummary>>([.. Kept.AsEnumerable().Reverse().Select(r =>
            new SavedRouteSummary(r.Id, r.Name, new DateTimeOffset(2026, 11, 7, 9, r.Id, 0, TimeSpan.Zero), r.Loop.Breakdown.LengthM / 1000) {
            UserStars = Rated.GetValueOrDefault(r.Id),
            Scored = Scores.GetValueOrDefault(r.Id) is not null,
            Stars = Scores.GetValueOrDefault(r.Id) is { } s ? WendYourWay.Core.Scoring.Stars.Of(s.Activity, s.Score, s.MatchedShare, s.OffRoadShare, s.ClimbPerKm) : null,
            MapChanged = MapChanged.Contains(r.Id),
        })]);

    public Task<SavedRoute?> OpenAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Kept.Where(r => r.Id == id).Select(r => new SavedRoute(r.Id, r.Name, r.Loop)).FirstOrDefault());

    public Task<IReadOnlyList<(SegmentKey Key, bool Forward)>> TraversalsAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Kept.Where(r => r.Id == id).Select(r => r.Traversals).FirstOrDefault() ?? []);

    public Task RenameAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        var at = Kept.FindIndex(r => r.Id == id);
        if (at >= 0) Kept[at] = Kept[at] with { Name = name.Trim() };
        return Task.CompletedTask;
    }

    public Task RateAsync(int id, int? stars, CancellationToken cancellationToken = default)
    {
        Rated[id] = stars;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        Kept.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }
}
