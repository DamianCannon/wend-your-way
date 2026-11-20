using Bunit;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Places;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Web.Components;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Data.Planning;
using WendYourWay.Data.Places;
using WendYourWay.Web.Planning;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The planner the screen asks for Loops (moved out of AskingForALoop, T118): answers set by a spec,
/// and every Request, Point-to-point, import and reopening it was asked for, kept to look at.
/// </summary>
internal sealed class FakePlanner : ILoopPlanner
{
    /// <summary>Each Point-to-point asked for (T091): from, to, of what Distance, at which Leans.</summary>
    public List<(LatLon Start, LatLon Finish, double? DistanceKm, Leans Leans)> RoutesAsked { get; } = [];

    /// <summary>The shortest way the routes offered are labelled against.</summary>
    public double ShortestM { get; set; } = 8410;

    /// <summary>When set, Point-to-points fail with it.</summary>
    public Exception? RouteFailure { get; set; }

    public Task<IOfferedLoops> PlanPointToPointAsync(
        LatLon start, LatLon finish, double? distanceKm = null, Leans? leans = null, Activity activity = Activity.Run, CancellationToken cancellationToken = default)
    {
        RoutesAsked.Add((start, finish, distanceKm, leans ?? Leans.Centre));
        if (RouteFailure is not null) return Task.FromException<IOfferedLoops>(RouteFailure);
        return Task.FromResult<IOfferedLoops>(new FakeOffer(Answers ?? OneOrNone(Answer), Leaning) { ShortestM = ShortestM });
    }

    public List<(LatLon Start, double DistanceKm)> Asked { get; } = [];

    public List<Leans> LeansAsked { get; } = [];

    public PlannedLoop? Answer { get; set; }

    /// <summary>When set, the Loops offered, in place of <see cref="Answer"/> alone.</summary>
    public IReadOnlyList<PlannedLoop>? Answers { get; set; }

    /// <summary>When set, Requests wait for it instead of answering at once.</summary>
    public TaskCompletionSource<PlannedLoop?>? Pending { get; set; }

    /// <summary>When set, Requests fail with it.</summary>
    public Exception? Failure { get; set; }

    /// <summary>What the Loops offered do when re-ranked or searched at a Lean.</summary>
    public Leaning Leaning { get; } = new();

    /// <summary>How far each Request says it has got, told before it answers (T054).</summary>
    public List<PlanningProgress> Script { get; } = [];

    /// <summary>Whether the last Request was cancelled.</summary>
    public bool Cancelled => _token.IsCancellationRequested;

    /// <summary>Every Request's token, in order, to see an earlier one cancelled.</summary>
    public List<CancellationToken> Tokens { get; } = [];

    private CancellationToken _token;

    /// <summary>The Activity each Request was for, in order.</summary>
    public List<Activity> ActivitiesAsked { get; } = [];

    /// <summary>Each GPX route imported (T083), and for which Activity.</summary>
    public List<(IReadOnlyList<LatLon> Route, Activity Activity)> ImportsAsked { get; } = [];

    /// <summary>An imported route, as matched.</summary>
    public PlannedLoop? Imported { get; set; }

    /// <summary>When set, imports fail with it.</summary>
    public Exception? ImportFailure { get; set; }

    /// <summary>Each Saved Route reopened (T085), and for which Activity.</summary>
    public List<(PlannedLoop Saved, Activity Activity)> Reopened { get; } = [];

    /// <summary>When set, reopening fails with it.</summary>
    public Exception? ReopenFailure { get; set; }

    /// <summary>When set, reopening waits for it.</summary>
    public TaskCompletionSource? ReopenPending { get; set; }

    /// <summary>When set, what a Saved Route reopens as: with its notes worked out (T157), say.</summary>
    public PlannedLoop? ReopenedAs { get; set; }

    public async Task<IOfferedLoops> ReopenAsync(
        PlannedLoop saved, IReadOnlyList<(WendYourWay.Core.Network.SegmentKey Key, bool Forward)> traversals, Activity activity = Activity.Run, CancellationToken cancellationToken = default)
    {
        Reopened.Add((saved, activity));
        if (ReopenPending is not null) await ReopenPending.Task;
        if (ReopenFailure is not null) throw ReopenFailure;
        return Offer([ReopenedAs ?? saved]);
    }

    public Task<IOfferedLoops> ImportAsync(IReadOnlyList<LatLon> route, Activity activity = Activity.Run, CancellationToken cancellationToken = default)
    {
        ImportsAsked.Add((route, activity));
        if (ImportFailure is not null) return Task.FromException<IOfferedLoops>(ImportFailure);
        return Task.FromResult<IOfferedLoops>(Offer(OneOrNone(Imported)));
    }

    public Task<IOfferedLoops> PlanAsync(LatLon start, double distanceKm, Leans? leans = null, Activity activity = Activity.Run, Action<PlanningProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Asked.Add((start, distanceKm));
        LeansAsked.Add(leans ?? Leans.Centre);
        ActivitiesAsked.Add(activity);
        _token = cancellationToken;
        Tokens.Add(cancellationToken);
        foreach (var step in Script) progress?.Invoke(step);
        if (Failure is not null) return Task.FromException<IOfferedLoops>(Failure);
        return Pending is null ? Task.FromResult<IOfferedLoops>(Offer(Answers ?? OneOrNone(Answer))) : WaitAsync(Pending);
    }

    private FakeOffer Offer(IReadOnlyList<PlannedLoop> loops) => new(loops, Leaning);

    private static IReadOnlyList<PlannedLoop> OneOrNone(PlannedLoop? loop) => loop is null ? [] : [loop];

    private async Task<IOfferedLoops> WaitAsync(TaskCompletionSource<PlannedLoop?> pending) => Offer(OneOrNone(await pending.Task));
}
