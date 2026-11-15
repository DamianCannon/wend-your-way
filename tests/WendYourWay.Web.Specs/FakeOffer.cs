using System.Collections;
using WendYourWay.Core.Loops;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>What the stand-in Loops offered do when re-ranked or searched at a Lean, and the Leans asked for.</summary>
internal sealed class Leaning
{
    /// <summary>When set, what the Loops are re-ranked to at some Leans (null: the same).</summary>
    public Func<Leans, IReadOnlyList<PlannedLoop>?>? ReRanked { get; set; }

    /// <summary>The Leans the Loops were re-ranked to.</summary>
    public List<Leans> ReRanks { get; } = [];

    /// <summary>When set, what a background search at some Leans offers (null: the same).</summary>
    public Func<Leans, IReadOnlyList<PlannedLoop>?>? Searched { get; set; }

    /// <summary>The Leans searched at.</summary>
    public List<Leans> Searches { get; } = [];

    /// <summary>How far each search says it has got, told before it waits or answers (T054).</summary>
    public List<PlanningProgress> SearchScript { get; } = [];

    /// <summary>When set, searches wait for it before answering.</summary>
    public TaskCompletionSource? SearchGate { get; set; }

    /// <summary>What a Nudge of a Loop gives (T076): by default, none.</summary>
    public Func<PlannedLoop, LoopNudge, NudgedLoop?>? Nudged { get; set; }

    /// <summary>The Nudges asked for, of which Loop.</summary>
    public List<(PlannedLoop Loop, LoopNudge Nudge)> Nudges { get; } = [];
}

/// <summary>A stand-in for the Loops a planner offers, re-ranked and searched as <paramref name="leaning"/> says.</summary>
internal sealed class FakeOffer(IReadOnlyList<PlannedLoop> loops, Leaning? leaning = null) : IOfferedLoops
{
    private readonly Leaning _leaning = leaning ?? new Leaning();

    public PlannedLoop this[int index] => loops[index];

    public int Count => loops.Count;

    /// <summary>For Point-to-points (T091), the shortest way between their ends.</summary>
    public double? ShortestM { get; init; }

    public Task<IOfferedLoops> ReRankAsync(Leans leans, CancellationToken cancellationToken = default)
    {
        _leaning.ReRanks.Add(leans);
        return Task.FromResult<IOfferedLoops>(new FakeOffer(_leaning.ReRanked?.Invoke(leans) ?? loops, _leaning));
    }

    public async Task<IOfferedLoops> SearchAsync(Leans leans, Action<PlanningProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        _leaning.Searches.Add(leans);
        foreach (var step in _leaning.SearchScript) progress?.Invoke(step);
        if (_leaning.SearchGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
        return new FakeOffer(_leaning.Searched?.Invoke(leans) ?? loops, _leaning);
    }

    /// <summary>What every Loop offered is made of, for saving (T080).</summary>
    public static readonly IReadOnlyList<(WendYourWay.Core.Network.SegmentKey Key, bool Forward)> Traversals =
        [(new WendYourWay.Core.Network.SegmentKey(11, 1, 2), true), (new WendYourWay.Core.Network.SegmentKey(12, 2, 1), false)];

    public IReadOnlyList<(WendYourWay.Core.Network.SegmentKey Key, bool Forward)>? TraversalsOf(PlannedLoop planned) => Traversals;

    /// <summary>The app's score for every Loop offered (T097): a Run's, 4 stars' worth.</summary>
    public static readonly RouteScore Score = new(WendYourWay.Core.Loops.Activity.Run, 0.3, 1, OffRoadShare: 0.5, ClimbPerKm: 10);

    public RouteScore? ScoreOf(PlannedLoop planned) => Score;

    /// <summary>The Segment any place on a Loop is on (T082).</summary>
    public static readonly WendYourWay.Core.Network.SegmentKey SegmentThere = new(11, 1, 2);

    public Task<WendYourWay.Core.Network.SegmentKey?> SegmentAtAsync(PlannedLoop planned, WendYourWay.Data.LatLon at, CancellationToken cancellationToken = default) =>
        Task.FromResult<WendYourWay.Core.Network.SegmentKey?>(SegmentThere);

    public Task<NudgedLoop?> NudgeAsync(PlannedLoop planned, LoopNudge nudge, Leans leans, CancellationToken cancellationToken = default)
    {
        _leaning.Nudges.Add((planned, nudge));
        return Task.FromResult(_leaning.Nudged?.Invoke(planned, nudge));
    }

    public IEnumerator<PlannedLoop> GetEnumerator() => loops.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
