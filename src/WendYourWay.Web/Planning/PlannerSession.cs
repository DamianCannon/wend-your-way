using System.Globalization;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Places;
using WendYourWay.Data;
using WendYourWay.Data.Places;
using WendYourWay.Data.Planning;
using WendYourWay.Data.Outings;
using WendYourWay.Web.Components;
using WendYourWay.Web.Phone;
using Microsoft.JSInterop;

namespace WendYourWay.Web.Planning;

/// <summary>
/// The planner's state and what the user does to it (T124c, from Planner.razor):
/// the Request, the Loops offered and the one shown, the history, the car parks around here, and
/// every transition: Go, routes to a finish, a start or finish chosen, re-ranking, paging, Nudges,
/// saving, opening, importing, Undo and Redo. Moved as it was, but that it raises
/// <see cref="Changed"/> where the page re-rendered, and draws on an <see cref="IPlannerMap"/>,
/// given once the map is first drawn. The page shows it, and keeps only what's its own.
/// </summary>
internal sealed class PlannerSession(
    ILoopPlanner loopPlanner, ISavedRoutes store, IMarks marks, PlannerSettings settings, ILastRequest lastRequest,
    ICarParks carParks, ILoopPreviews previews, IPlaceSearch places, IRouteTurns turns, IPhoneShare phoneShare,
    INetworkDate networkDate, ILogger<PlannerSession> logger) : IDisposable
{
    private readonly ILogger<PlannerSession> _logger = logger;
    private IPlannerMap _map = null!;

    /// <summary>Raised where the page should show what's changed.</summary>
    internal event Action? Changed;
    private LatLon? _start;
    private double _distanceKm = 10;

    // The Hills Lean (T050): −1 fewer hills, +1 more, 0 neither. Go searches at it; moving it once
    // Loops are offered re-ranks the Loops found (T053).
    private double _hillsLean;
    // The Off-road Lean (T058): −1 tarmac, +1 trails, 0 neither; as the Hills Lean.
    private double _offRoadLean;
    // The route off the map, to see the ground under it (T071): kept while paging, not between visits.
    private bool _routeHidden;

    // One history of what's been on screen (T078): a step for each Request's Loops and each
    // Nudge, the Loop paged to and the Leans' re-ranking kept in the step they belong to. The last
    // RequestsKept Requests' steps only, their Loops still nudgeable.
    private sealed record Step(int Request, IOfferedLoops? Offered, IReadOnlyList<PlannedLoop> Loops, int Current,
        LatLon? Start, FoundStart? Chosen, double DistanceKm, Activity Activity, string? Imported,
        bool EndElsewhere = false, LatLon? Finish = null, string? FinishName = null, double? FinishDistanceKm = null, string? StartName = null);

    private readonly PlannerHistory<Step> _history = new();

    // What the last Nudge said (T076), or that a route was saved (T080).
    private string? _nudgeNote;

    // A Saved Route opened, if that's what's shown (T080): saved again, its own Segments are kept.
    private int? _openedRoute;
    // A GPX route imported, if that's what's shown (T083): its name, offered for saving it.
    private string? _importedName;

    // The names Loops were saved or opened by (T112), each Loop by itself.
    private readonly SavedNames _savedNames = new();

    // What the Loop shown is exported as (T112, user: "Its own name, else place").
    private string ExportName => _savedNames.ExportName(Shown, SuggestedName);
    // A Saved Route opened being made ready to nudge (T085): its offer once its slice is read, or
    // why it can't be.
    private Task<(IOfferedLoops? Offered, string? Problem)>? _reopening;
    // How many routes have been saved, for the Saved Routes panel to read its list again (T083).
    private int _saves;
    private Activity _activity;

    // A Point-to-point (T091): ending elsewhere, where (null till chosen), what it's called if a
    // place searched for, and its Distance, or none for a spread from shortest to greenest.
    private bool _endElsewhere;
    private LatLon? _finish;
    private string? _finishName;
    private double? _finishDistanceKm;
    // Which box a map click fills (T093): the one last clicked into, the Start's till then.
    private bool _fillFinish;
    // A start clicked on the map or dragged: its nearest postcode, once found.
    private string? _startName;

    internal string FinishLabel => RouteNames.Finish(_endElsewhere, _finish, _finishName);
    private Leans Leans => new(_hillsLean, _offRoadLean);
    // Where to start: the car parks around here (T060), those in view of the map, read as the user
    // leaves it, from zoom 11; and the one the start is at. No drive to choose: "we can just select
    // from the car parks shown on the map and decide for ourself how far away they might be"
    // (user, 2026-11-02).
    private FoundStart? _chosen;
    // Car parks around here and their comparing (T124d): told what they need of the Request.
    private readonly StartsAround _starts = new(carParks, previews, logger);
    internal IReadOnlyList<FoundStart>? Listed => _starts.Listed;
    // The Loops offered (R1.4), best first, and which is shown: the map shows only that one.
    private IReadOnlyList<PlannedLoop> _loops = [];
    // The same, re-rankable, once a Request has found them; and the re-rank in flight, if any,
    // with the background search that follows it once the slider stays still (T053b).
    private IOfferedLoops? _offered;
    private CancellationTokenSource? _reRank;
    private bool _searching;
    private static readonly TimeSpan StillFor = TimeSpan.FromSeconds(0.4);
    private int _current;
    private PlannedLoop? Shown => _current < _loops.Count ? _loops[_current] : null;
    private bool _finding;
    // How far the Request being found has got (T054), and the Loops it has offered so far.
    private PlanningProgress? _progress;
    private IOfferedLoops? _streamed;
    private bool _nothingFound;
    private string? _problem;

    // An Outing on the map: the Loop and its pager card step aside until the Request panel is
    // opened again, so the two are never drawn over each other (user, 2026-10-19).
    private bool _outingShown;

    // The side panel open, by its label: the Request's at first.
    private string _panel = "Request";

    // The Request in flight, if any. A new start or a new Request cancels it, so a Loop found for
    // an old start never appears under a new one.
    private CancellationTokenSource? _request;

    internal string StartLabel => RouteNames.Start(_start, settings.Home, _chosen, _startName);


    // The last Request's settings, or home, 10 km and Hills at its centre before any (R1.1a, T056).
    internal async Task InitializeAsync()
    {
        _starts.Changed += () => Changed?.Invoke();
        _start = settings.Home;
        try
        {
            if (await lastRequest.LoadAsync() is { } last)
            {
                (_start, _distanceKm, _hillsLean, _offRoadLean, _activity) = (last.Start ?? settings.Home, last.DistanceKm, last.HillsLean, last.OffRoadLean, last.Activity);
                (_endElsewhere, _finish, _finishDistanceKm, _finishName) = (last.Finish is not null, last.Finish, last.FinishDistanceKm, last.FinishName);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Not knowing the last Request is no reason not to plan the next.
            Log.SettingsNotKept(_logger, e);
        }
        try
        {
            _osmDataAt = await networkDate.OsmDataAtAsync();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Not knowing its date is no reason not to plan: none is shown.
            Log.SettingsNotKept(_logger, e);
        }
    }

    // The date of the OpenStreetMap extract the Network was built from (T117), if kept.
    private DateTimeOffset? _osmDataAt;

    /// <summary>Keeps the settings as they are now, for next time; home as home, never its position.</summary>
    private async Task KeepSettingsAsync()
    {
        try
        {
            await lastRequest.SaveAsync(new RequestSettings(_start == settings.Home ? null : _start, _distanceKm, _hillsLean, _offRoadLean, _activity)
            {
                Finish = _endElsewhere ? _finish : null,
                FinishDistanceKm = _endElsewhere ? _finishDistanceKm : null,
                FinishName = _endElsewhere ? _finishName : null,
            });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.SettingsNotKept(_logger, e);
        }
    }

    /// <summary>The map drawn for the first time: the start, the finish and the Marks on it.</summary>
    internal async Task MapReadyAsync(IPlannerMap map)
    {
        _map = map;
        if (_start is { } start) await _map.ShowStartAsync(start);
        if (_endElsewhere && _finish is { } finish) await _map.ShowFinishAsync(finish);
        await ShowMarksAsync();
    }

    internal async Task GoAsync()
    {
        if (_start is not { } start || _finding) return;
        if (_endElsewhere)
        {
            await GoToFinishAsync(start);
            return;
        }
        if (_distanceKm is not (>= WendYourWay.Core.Loops.Request.MinDistanceKm and <= WendYourWay.Core.Loops.Request.MaxDistanceKm))
        {
            _problem = $"Choose a Distance from {WendYourWay.Core.Loops.Request.MinDistanceKm:0} to {WendYourWay.Core.Loops.Request.MaxDistanceKm:0} km.";
            return;
        }

        var request = Restart();
        _finding = true;
        Compare();
        var leans = Leans;
        await KeepSettingsAsync();
        // The last Loop goes at once (user, 2026-10-26).
        await _map.ClearLoopAsync();
        IOfferedLoops loops;
        try
        {
            loops = await loopPlanner.PlanAsync(start, _distanceKm, leans, _activity, p => _ = ProgressAsync(p, request.Token), request.Token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e) when (e is OutsideEnglandAndWalesException or NetworkNeedsRebuildException && !request.IsCancellationRequested)
        {
            StopFinding();
            _problem = e.Message;
            return;
        }
        catch (Exception e) when (e is not OperationCanceledException && !request.IsCancellationRequested)
        {
            // A Request that fails for any reason (the database stopped, say) must say so and leave
            // the screen usable, not take the whole page down.
            Log.PlanningFailed(_logger, e);
            StopFinding();
            _problem = "Couldn't plan a Loop. The app's log says why.";
            return;
        }
        if (request.IsCancellationRequested) return;

        StopFinding();
        _offered = loops;
        await ShowAsync(loops);
        Take(_history.NextRequest());
        // A slider moved while the Loops were being found.
        if (Leans != leans) await ReRankAsync();
    }

    /// <summary>
    /// Point-to-points from the start to the finish (T091), of the Distance or, with none, a spread
    /// from shortest to greenest: a step of the history, as a Request's Loops are.
    /// </summary>
    private async Task GoToFinishAsync(LatLon start)
    {
        if (_finish is not { } finish)
        {
            _problem = "Choose where to finish: click the map or search for a place.";
            return;
        }
        if (_finishDistanceKm is { } km && km is not (>= WendYourWay.Core.Loops.Request.MinDistanceKm and <= WendYourWay.Core.Loops.Request.MaxDistanceKm))
        {
            _problem = $"Choose a Distance from {WendYourWay.Core.Loops.Request.MinDistanceKm:0} to {WendYourWay.Core.Loops.Request.MaxDistanceKm:0} km, or none.";
            return;
        }

        var request = Restart();
        _finding = true;
        Compare();
        var leans = Leans;
        await KeepSettingsAsync();
        await _map.ClearLoopAsync();
        IOfferedLoops routes;
        try
        {
            routes = await loopPlanner.PlanPointToPointAsync(start, finish, _finishDistanceKm, leans, _activity, request.Token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e) when (e is PointToPointRefusedException or OutsideEnglandAndWalesException or NetworkNeedsRebuildException && !request.IsCancellationRequested)
        {
            StopFinding();
            _problem = e.Message;
            return;
        }
        catch (Exception e) when (e is not OperationCanceledException && !request.IsCancellationRequested)
        {
            Log.PlanningFailed(_logger, e);
            StopFinding();
            _problem = "Couldn't plan a route. The app's log says why.";
            return;
        }
        if (request.IsCancellationRequested) return;

        StopFinding();
        _offered = routes;
        await ShowAsync(routes);
        Take(_history.NextRequest());
        // A slider moved while they were being found: found again at it.
        if (Leans != leans) await ReRankAsync();
    }

    /// <summary>
    /// The Finish emptied, or its pin removed (T091, T093): a Loop again, found at once as a start
    /// chosen finds them.
    /// </summary>
    internal async Task FinishAtStartAsync()
    {
        if (!_endElsewhere) return;
        (_endElsewhere, _finish, _finishName) = (false, null, null);
        await _map.ClearFinishAsync();
        Changed?.Invoke();
        await GoAsync();
    }

    /// <summary>The finish set (T091): shown, and the routes to it found.</summary>
    private async Task FinishAtAsync(LatLon finish, string? name)
    {
        (_endElsewhere, _finish, _finishName) = (true, finish, name);
        await _map.ShowFinishAsync(finish);
        await GoAsync();
    }

    /// <summary>
    /// The start and finish swapped (T094): each box showing the other's name, both pins moved, and
    /// the routes found the other way at once.
    /// </summary>
    internal async Task SwapEndsAsync()
    {
        if (!_endElsewhere || _start is not { } start || _finish is not { } finish) return;
        var startName = StartLabel;
        var finishName = _finishName;
        await ChooseStartAsync(finish);
        (_startName, _finish, _finishName) = (finishName, start, startName);
        await _map.ShowFinishAsync(start);
        Changed?.Invoke();
        await GoAsync();
    }

    /// <summary>A place chosen in the Finish box (T093): the finish, whatever kind of place it is.</summary>
    internal Task FinishPlaceChosenAsync(FoundPlace place) => FinishAtAsync(place.At, place.Name);

    // A finish clicked on the map or dragged: routes to it at once, its postcode in its box after.
    private async Task FinishOnTheMapAsync(LatLon at)
    {
        var routes = FinishAtAsync(at, null);
        _finishName = await PostcodeAtAsync(at);
        Changed?.Invoke();
        await routes;
        // Kept with its name, found after the routes were asked for.
        Amend();
        await KeepSettingsAsync();
    }

    internal Task FinishDraggedAsync(LatLon to) => _endElsewhere ? FinishOnTheMapAsync(to) : Task.CompletedTask;

    internal async Task StartDraggedAsync(LatLon to)
    {
        await ChooseStartAsync(to);
        var found = GoAsync();
        _startName = await PostcodeAtAsync(to);
        Changed?.Invoke();
        await found;
        Amend();
    }

    // The postcode nearest a place clicked or dropped on the map, for its box; none if it can't be found.
    private async Task<string?> PostcodeAtAsync(LatLon at)
    {
        try
        {
            return (await places.NearestPostcodeAsync(at))?.Name;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.SearchFailed(_logger, e);
            return null;
        }
    }

    /// <summary>
    /// Re-ranks the Loops found at the slider's Lean, with no new search (T053, R10.4); then, once
    /// the slider has stayed still, searches at that Lean too, adding what it finds (T053b, ADR
    /// 0007). The latest move wins.
    /// </summary>
    internal async Task ReRankAsync()
    {
        Compare();
        if (_offered is not { } offered) return;
        await KeepSettingsAsync();
        _reRank?.Cancel();
        _reRank?.Dispose();
        var reRank = _reRank = new CancellationTokenSource();
        var leans = Leans;
        // Point-to-points can't be re-ranked yet (T091): found again once the slider stays still.
        if (offered.ShortestM is not null)
        {
            try
            {
                await Task.Delay(StillFor, reRank.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await GoAsync();
            return;
        }
        try
        {
            await OfferAsync(await offered.ReRankAsync(leans, reRank.Token), reRank.Token);
            _searching = true;
            Changed?.Invoke();
            await Task.Delay(StillFor, reRank.Token);
            await OfferAsync(await offered.SearchAsync(leans, p => Searched(p, reRank.Token), reRank.Token), reRank.Token);
        }
        catch (OperationCanceledException) when (reRank.IsCancellationRequested)
        {
            // A newer move, a new Request or a new start took over.
        }
        catch (Exception e) when (e is not OperationCanceledException && !reRank.IsCancellationRequested)
        {
            Log.PlanningFailed(_logger, e);
            _problem = "Couldn't plan a Loop. The app's log says why.";
        }
        finally
        {
            if (reRank == _reRank) (_searching, _progress) = (false, null);
        }
    }

    /// <summary>
    /// How far the background search has got (T054): said in the pager card. The Loops it tried
    /// were drawn faintly too, till the user found them "a bit annoying" (2026-10-29).
    /// </summary>
    private void Searched(PlanningProgress progress, CancellationToken reRank)
    {
        if (reRank.IsCancellationRequested || !_searching) return;
        _progress = progress;
        Changed?.Invoke();
    }

    /// <summary>Offers these Loops in place of those offered.</summary>
    private async Task OfferAsync(IOfferedLoops loops, CancellationToken reRank)
    {
        reRank.ThrowIfCancellationRequested();
        _offered = loops;
        await ShowAsync(loops);
        // Re-ranked, not a new step: the Loops of the step they were re-ranked from.
        Amend();
    }

    /// <summary>
    /// Shows these Loops in the pager and on the map, keeping the place paged to, or the last Loop
    /// if there are fewer now (user, 2026-10-27): a slider move never pages for the user.
    /// </summary>
    private async Task ShowAsync(IReadOnlyList<PlannedLoop> loops)
    {
        _nudgeNote = null;
        (_loops, _current) = (loops, Math.Clamp(_current, 0, Math.Max(loops.Count - 1, 0)));
        _nothingFound = loops.Count == 0;
        Changed?.Invoke();
        if (_outingShown) return;
        if (Shown is { } now) await DrawLoopAsync(now);
        else await _map.ClearLoopAsync();
    }

    /// <summary>
    /// How far the Request has got (T054): said in the pager card, and the Loops found so far
    /// offered, until it's done.
    /// </summary>
    private async Task ProgressAsync(PlanningProgress progress, CancellationToken request)
    {
        if (request.IsCancellationRequested || !_finding) return;
        _progress = progress;
        Changed?.Invoke();
        try
        {
            if (progress.SoFar is { Count: > 0 } soFar && soFar != _streamed && !request.IsCancellationRequested)
            {
                _streamed = soFar;
                await ShowAsync(soFar);
            }
        }
        catch (JSDisconnectedException)
        {
            // The page has gone.
        }
    }

    /// <summary>The Request is done, one way or another: the progress goes.</summary>
    private void StopFinding()
    {
        (_finding, _progress, _streamed) = (false, null, null);
        Compare();
    }

    /// <summary>
    /// Shows the next Loop offered (+1) or the one before (−1), round from the last to the first and
    /// back ("the left/right arrows should wrap around", user, 2026-10-29).
    /// </summary>
    internal async Task PageAsync(int step)
    {
        if (_outingShown || _loops.Count < 2) return;
        var to = (((_current + step) % _loops.Count) + _loops.Count) % _loops.Count;
        _current = to;
        _nudgeNote = null;
        Amend();
        await DrawLoopAsync(_loops[to]);
    }

    /// <summary>
    /// The Loop shown nudged (T076): dragged to go through a place, or a bit of it avoided; the
    /// Loop before kept for Undo. Told if it's now out of the Request's window, or there was no way.
    /// </summary>
    internal async Task NudgeAsync(LoopNudge nudge)
    {
        if (_outingShown || Shown is not { } shown) return;
        if (await NudgeableAsync() is not { } offered) return;
        NudgedLoop? nudged;
        try
        {
            nudged = await offered.NudgeAsync(shown, nudge, Leans);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.PlanningFailed(_logger, e);
            nudged = null;
        }
        if (nudged is null)
        {
            _nudgeNote = NudgeNotes.Failed(nudge);
            Changed?.Invoke();
            return;
        }
        _nudgeNote = NudgeNotes.Made(nudged.KmOutsideWindow);
        // A nudge keeps the name it was saved or opened by (T112).
        _savedNames.Carry(_loops[_current], nudged.Loop);
        _loops = [.. _loops.Select((l, i) => i == _current ? nudged.Loop : l)];
        Changed?.Invoke();
        await _map.ShowLoopAsync(nudged.Loop, fit: false);
        Take(_history.CurrentRequest!.Value);
    }

    // The name offered for saving the Loop shown (T080): where it starts and its km.
    internal string SuggestedName => RouteNames.Suggested(_importedName, Shown, toAFinish: _offered?.ShortestM is not null, StartLabel, FinishLabel);

    /// <summary>The Loop shown kept by this name under Saved Routes (T080).</summary>
    internal async Task SaveAsync(string name)
    {
        if (Shown is not { } shown) return;
        var traversals = _offered?.TraversalsOf(shown) ?? (_openedRoute is { } id ? await store.TraversalsAsync(id) : null);
        if (traversals is null) return;
        try
        {
            // The app's score for it, for its stars (T097): none for a Saved Route opened again.
            var saved = await store.SaveAsync(name, shown, traversals, _offered?.ScoreOf(shown));
            if (_ratedAs is { } rated && rated.Loop == shown) await store.RateAsync(saved, rated.Stars);
            _savedNames.Saved(shown, name);
            _nudgeNote = $"Saved as {name}";
            _saves++;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.PlanningFailed(_logger, e);
            _nudgeNote = "Couldn't save it. The app's log says why.";
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// The Loops shown, to nudge: those offered, or a Saved Route opened once it's ready (T085),
    /// waited for. None, saying why if it can't be.
    /// </summary>
    private async Task<IOfferedLoops?> NudgeableAsync()
    {
        if (_offered is { } offered) return offered;
        if (_reopening is not { } reopening)
        {
            if (_openedRoute is not null)
            {
                _nudgeNote = "Open it again from Saved Routes to nudge it";
                Changed?.Invoke();
            }
            return null;
        }
        var (ready, problem) = await reopening;
        if (problem is not null)
        {
            _nudgeNote = problem;
            Changed?.Invoke();
        }
        return reopening == _reopening ? ready : null;
    }

    // A Saved Route made ready to nudge: its slice read and its Segments found again (T085).
    private async Task<(IOfferedLoops?, string?)> ReopenAsync(SavedRoute route, CancellationToken cancellationToken)
    {
        try
        {
            var traversals = await store.TraversalsAsync(route.Id, cancellationToken);
            return (await loopPlanner.ReopenAsync(route.Loop, traversals, _activity, cancellationToken), null);
        }
        catch (RouteNotMatchedException e)
        {
            return (null, e.Message);
        }
        catch (OperationCanceledException)
        {
            return (null, null);
        }
        catch (Exception e)
        {
            Log.PlanningFailed(_logger, e);
            return (null, "Couldn't get it ready to nudge. The app's log says why.");
        }
    }

    /// <summary>A Saved Route on the map in the pager (T080): a step of the history, as a Request's Loops are.</summary>
    internal async Task OpenSavedAsync(SavedRoute route)
    {
        var request = Restart();
        (_offered, _loops, _current) = (null, [route.Loop], 0);
        (_start, _chosen) = (route.Loop.Start, null);
        _openedRoute = route.Id;
        _savedNames.Opened(route.Loop, route.Name);
        _nudgeNote = null;
        _outingShown = false;
        Changed?.Invoke();
        await _map.ShowStartAsync(route.Loop.Start);
        await DrawLoopAsync(route.Loop);
        await EndsOfAsync(route.Loop);
        Take(_history.NextRequest());
        await NameEndsAsync(route.Loop);

        // Made ready to nudge behind it (T085), not waited for: shown at once, nudgeable once it is.
        _ = ReadyAsync(_reopening = ReopenAsync(route, request.Token), request.Token);
    }

    // A route opened or imported (T092): one not back at its start a Point-to-point, its finish at
    // its end; a Loop otherwise, with no finish.
    private async Task EndsOfAsync(PlannedLoop route)
    {
        if (!route.EndsAtStart)
        {
            (_endElsewhere, _finish, _finishName) = (true, route.Line[^1], null);
            await _map.ShowFinishAsync(route.Line[^1]);
        }
        else if (_endElsewhere)
        {
            (_endElsewhere, _finish, _finishName) = (false, null, null);
            await _map.ClearFinishAsync();
        }
    }

    // Its ends' nearest postcodes in their boxes, once found, with its step of the history.
    private async Task NameEndsAsync(PlannedLoop route)
    {
        _startName = route.Start == settings.Home ? null : await PostcodeAtAsync(route.Start);
        if (_endElsewhere) _finishName = await PostcodeAtAsync(route.Line[^1]);
        Changed?.Invoke();
        Amend();
    }

    // A Saved Route's step of the history nudgeable once it's ready, if it's still the one shown.
    private async Task ReadyAsync(Task<(IOfferedLoops? Offered, string? Problem)> reopening, CancellationToken request)
    {
        var (offered, _) = await reopening;
        if (offered is null || reopening != _reopening || request.IsCancellationRequested) return;
        _offered = offered;
        Amend();
    }

    /// <summary>
    /// A GPX route (T083) matched for the Activity chosen and shown alone, as a Request's Loops are:
    /// a step of the history. If it can't be, why, and what was shown stays.
    /// </summary>
    internal Task<string?> ImportAsync(RouteGpx gpx) => ShowRouteAsync(gpx.Points, gpx.Name, _activity);

    /// <summary>
    /// An Outing kept as a route (T084): its track matched for its own Activity as an imported
    /// GPX is, the Activity chosen made its, the Outing off the map.
    /// </summary>
    // Matched on every point it recorded, not the map's thinner line (user, 2026-11-09).
    // The user's stars for it go with the route as made, saved unnudged (T135).
    internal async Task<string?> MakeRouteAsync(OutingView outing, int? userStars = null)
    {
        var refused = await ShowRouteAsync(outing.RouteTrack.Count > 0 ? outing.RouteTrack : outing.Track, outing.Name, outing.Activity);
        if (refused is null && userStars is { } stars && Shown is { } made) _ratedAs = (made, stars);
        return refused;
    }

    // The route made from an Outing the user rated, and their stars for it (T135).
    private (PlannedLoop Loop, int Stars)? _ratedAs;

    // A route from elsewhere shown alone, as a Request's Loops are: a step of the history. If it
    // can't be, why, and what was shown stays.
    private async Task<string?> ShowRouteAsync(IReadOnlyList<LatLon> route, string? name, Activity activity)
    {
        IOfferedLoops loops;
        try
        {
            loops = await loopPlanner.ImportAsync(route, activity);
        }
        catch (Exception e) when (e is RouteNotMatchedException or OutsideEnglandAndWalesException or NetworkNeedsRebuildException)
        {
            return e.Message;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.PlanningFailed(_logger, e);
            return "Couldn't import it. The app's log says why.";
        }
        if (loops.Count == 0) return "Couldn't match it to the map.";

        Restart();
        _offered = loops;
        (_importedName, _activity) = (name, activity);
        (_start, _chosen) = (loops[0].Start, null);
        if (_outingShown) await _map.ClearOutingAsync();
        _outingShown = false;
        await _map.ShowStartAsync(loops[0].Start);
        await ShowAsync(loops);
        await EndsOfAsync(loops[0]);
        Take(_history.NextRequest());
        await NameEndsAsync(loops[0]);
        // Called by the panel, not as an event of this page: drawn again here, with Undo.
        Changed?.Invoke();
        return null;
    }

    /// <summary>
    /// The Segment of the Loop shown at <paramref name="at"/> Blocked (T082): no Loop uses it from now
    /// on, and the Loop shown goes round it at once, as an Avoid.
    /// </summary>
    internal async Task BlockAsync(LatLon at)
    {
        if (Shown is not { } shown || await NudgeableAsync() is not { } offered) return;
        if (await offered.SegmentAtAsync(shown, at) is not { } segment)
        {
            _nudgeNote = "Nothing of the route there to block";
            Changed?.Invoke();
            return;
        }
        await marks.BlockAsync(segment);
        await ShowMarksAsync();
        await NudgeAsync(new AvoidNudge(at));
        _nudgeNote ??= "Blocked: no Loop will use it";
        Changed?.Invoke();
    }

    /// <summary>A Blocked Segment usable again (T082): Loops planned from now on may use it.</summary>
    internal async Task UnblockAsync(int id)
    {
        await marks.RemoveAsync(id);
        await ShowMarksAsync();
        _nudgeNote = "Unblocked: Loops planned from now on may use it";
        Changed?.Invoke();
    }

    private async Task ShowMarksAsync()
    {
        try
        {
            await _map.ShowMarksAsync([.. (await marks.ListAsync()).Where(m => m.Line.Count >= 2).Select(m => new MarkOnMap(m.Id, m.Line))]);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.PlanningFailed(_logger, e);
        }
    }

    // Undo while a Request is found takes back the Request itself.
    internal bool CanUndo => _history.CanUndo(_finding);

    internal bool CanRedo => _history.CanRedo(_finding);

    /// <summary>
    /// A step back through the history (T078): the last Nudge undone, or the last Request with its
    /// start, its Loops and their Nudges; a Request still being found stopped.
    /// </summary>
    internal async Task UndoAsync()
    {
        if (!CanUndo) return;
        var (step, fit) = _history.Undo(_finding);
        await RestoreAsync(step, fit);
    }

    /// <summary>A step forward again through what was undone (T078).</summary>
    internal async Task RedoAsync()
    {
        if (!CanRedo) return;
        var (step, fit) = _history.Redo();
        await RestoreAsync(step, fit);
    }

    // What's on screen now, a step of the history after any that could be redone, which go.
    private void Take(int request) =>
        _history.Take(request, new Step(request, _offered, _loops, _current, _start, _chosen, _distanceKm, _activity, _importedName, _endElsewhere, _finish, _finishName, _finishDistanceKm, _startName));

    // The step on screen as it now is: the Loop paged to, or the Loops re-ranked.
    private void Amend()
    {
        _history.Amend(_finding, step => step with { Offered = _offered, Loops = _loops, Current = _current, StartName = _startName, FinishName = _finishName });
    }

    // A step shown again: the map fitted to it only if it's another Request's (T087).
    private async Task RestoreAsync(Step step, bool fit)
    {
        Restart();
        (_offered, _loops, _current) = (step.Offered, step.Loops, step.Current);
        (_start, _chosen, _distanceKm, _activity, _importedName) = (step.Start, step.Chosen, step.DistanceKm, step.Activity, step.Imported);
        (_endElsewhere, _finish, _finishName, _finishDistanceKm, _startName) = (step.EndElsewhere, step.Finish, step.FinishName, step.FinishDistanceKm, step.StartName);
        _nudgeNote = null;
        Changed?.Invoke();
        if (_start is { } start) await _map.ShowStartAsync(start);
        if (_endElsewhere && _finish is { } finish) await _map.ShowFinishAsync(finish);
        else await _map.ClearFinishAsync();
        if (Shown is { } shown) await DrawLoopAsync(shown, fit);
        else await _map.ClearLoopAsync();
        await KeepSettingsAsync();
        Compare();
    }

    /// <summary>
    /// A start clicked on the map: its Loops are found, as a car park's or a postcode's are, and its
    /// postcode shows in the search box ("if I just click on the map to place the start then the
    /// floating panel just says 'No Loops yet' ... can the location search box update to show the
    /// clicked postcode?", user, 2026-11-03).
    /// </summary>
    internal async Task StartChosenOnMapAsync(LatLon start)
    {
        // With the Outings open a click sets no start: there are no Loops to find there (user,
        // 2026-11-09: "the orange dot which is a bit useless here"); nor with the Saved Routes open
        // (T137, user: "that's not the point of this view").
        if (_panel is "Outings" or "Saved Routes") return;
        // The Finish last clicked into, a click is the finish (T093).
        if (_fillFinish)
        {
            await FinishOnTheMapAsync(start);
            return;
        }
        await ChooseStartAsync(start);
        // Not waited for: the click is done, and the Loops come as they're found.
        _ = GoAsync();
        _startName = await PostcodeAtAsync(start);
        Changed?.Invoke();
        Amend();
    }

    private async Task ChooseStartAsync(LatLon start)
    {
        var wasChosen = _chosen is not null;
        (_start, _chosen, _startName) = (start, null, null);
        // A start clicked elsewhere: no place found is the start any more.
        if (wasChosen) await MarkStartsAsync();
        Restart();
        await _map.ShowStartAsync(start);
        await _map.ClearLoopAsync();
    }

    // Loop 1 from each of the first car parks listed, compared again (T062): never while finding.
    private void Compare() => _starts.Compare(_finding, _distanceKm, Leans, _activity);

    /// <summary>
    /// Draws a Loop on the map. A different route (the map fitted to it) shows the route again if it
    /// was hidden ("the route should be shown again and the toggle put back", user, 2026-11-09); a
    /// change to the one shown (a nudge) leaves it as it is.
    /// </summary>
    private async Task DrawLoopAsync(PlannedLoop loop, bool fit = true)
    {
        // A code sent for one Loop isn't left beside another (T099).
        if (fit) _phoneAsked = false;
        if (fit && _routeHidden)
        {
            _routeHidden = false;
            await _map.ShowRouteAsync(true);
            Changed?.Invoke();
        }
        await _map.ShowLoopAsync(loop, fit);
    }

    /// <summary>The route's show/hide button: one click either way (T071).</summary>
    internal async Task ToggleRouteAsync()
    {
        _routeHidden = !_routeHidden;
        await _map.ShowRouteAsync(!_routeHidden);
    }

    /// <summary>A Run or a Gravel Ride chosen: its Loops from the start, at once, as a start chosen finds them.</summary>
    internal async Task ChooseActivityAsync(Activity activity)
    {
        if (activity == _activity) return;
        _activity = activity;
        // Its own car parks (T073), compared for it; with no view yet, nothing to look at.
        if (_starts.View is null) Compare();
        else await LookAroundAsync();
        await GoAsync();
    }

    internal string? PreviewOf(FoundStart found) => _starts.PreviewOf(found, _distanceKm, Leans, _activity);


    /// <summary>
    /// A place searched for and chosen (T064): the map goes there and its car parks follow, as when
    /// the user moves it. A postcode is exact, so it's the start too, and its Loops are found, as a
    /// car park's are ("I want to see routes from that postcode as if I had clicked that
    /// location", user, 2026-11-03).
    /// </summary>
    /// <returns>Whether the start stayed where it was, for the Start box to show it again.</returns>
    internal async Task<bool> GoToPlaceAsync(FoundPlace place)
    {
        await _map.GoToAsync(place.At, place.Zoom);
        // In the Start box, the start, but for a postcode district, which only takes the map there (T064, T093).
        if (place.Kind == PlaceKind.District) return true;
        await StartAtAsync(new FoundStart(place.Name, place.At, 0));
        return false;
    }

    /// <summary>The user moved or zoomed the map: the car parks around here follow it (T060).</summary>
    internal async Task ViewChangedAsync(MapViewport viewport)
    {
        _starts.Viewed(viewport);
        await LookAroundAsync();
    }

    /// <summary>Reads the car parks in view and the best of them for the Distance, and marks them.</summary>
    internal async Task LookAroundAsync()
    {
        if (_starts.View is null) return;
        await _starts.ReadAsync(_distanceKm, _activity);
        await MarkStartsAsync();
        Compare();
    }

    // The car parks marked on the map, the start's marked as chosen.
    private Task MarkStartsAsync() => _starts.MarkAsync(_map, _chosen);

    internal async Task StartClickedAsync(int index)
    {
        var onMap = _starts.OnMap;
        if (index >= 0 && index < onMap.Count) await CarParkChosenAsync(onMap[index]);
    }

    // A car park chosen fills the box a map click would (T095): with the Finish marked, the finish.
    internal Task CarParkChosenAsync(FoundStart found) => _fillFinish ? FinishAtAsync(found.At, found.Name) : StartAtAsync(found);

    /// <summary>Makes a place found the start and finds its Loops.</summary>
    private async Task StartAtAsync(FoundStart found)
    {
        await ChooseStartAsync(found.At);
        _chosen = found;
        await MarkStartsAsync();
        await GoAsync();
    }

    /// <summary>Cancels any Request in flight and clears what it showed; returns the next one's token source.</summary>
    private CancellationTokenSource Restart()
    {
        _openedRoute = null;
        _importedName = null;
        _ratedAs = null;
        _reopening = null;
        _request?.Cancel();
        _request?.Dispose();
        _request = new CancellationTokenSource();
        _reRank?.Cancel();
        _reRank?.Dispose();
        _reRank = null;
        _searching = false;
        (_finding, _progress, _streamed) = (false, null, null);
        (_offered, _loops, _current) = (null, [], 0);
        _nothingFound = false;
        _problem = null;
        return _request;
    }

    /// <summary>Draws an Outing over the Segments it covered, with its gaps (STORY-030), in place of the Loop.</summary>
    // The Outing shown deleted, with none left to show (T106): it leaves the map.
    internal async Task OutingGoneAsync()
    {
        if (_outingShown) await _map.ClearOutingAsync();
        _outingShown = false;
    }

    internal async Task ShowOutingAsync(OutingView outing)
    {
        _outingShown = true;
        await _map.ClearLoopAsync();
        await _map.ShowOutingAsync(outing);
    }

    /// <summary>Back at the Request: the Outing goes, and the Loop it replaced comes back.</summary>
    internal async Task PanelOpenedAsync(string panel)
    {
        _panel = panel;
        if (panel != "Request" || !_outingShown) return;
        _outingShown = false;
        await _map.ClearOutingAsync();
        if (Shown is { } shown) await DrawLoopAsync(shown);
    }

    /// <summary>Marks the place pointed at on the elevation profile, or takes the mark away.</summary>
    internal Task MarkPlaceAsync(LatLon? place) => place is { } at ? _map.MarkPlaceAsync(at) : _map.ClearPlaceAsync();

    private bool _phoneAsked;
    internal WendYourWay.Web.Phone.PhoneLink? _phone;

    /// <summary>
    /// Sends the Loop shown to the phone (T099): a link to it on the home network, once, for ten
    /// minutes, shown as a QR code. The Loop as it is now: another shown later isn't sent.
    /// </summary>
    internal async Task PhoneAsync()
    {
        if (Shown is not { } shown) return;
        _phone = await phoneShare.ShareAsync(LoopGpx.Of(shown, await TurnsOfAsync(shown), ExportName));
        _phoneAsked = true;
    }



    // Its turn points (T100), from its Segments: none if they aren't known, or can't be read.
    private async Task<IReadOnlyList<RouteTurn>> TurnsOfAsync(PlannedLoop shown)
    {
        var traversals = _offered?.TraversalsOf(shown) ?? (_openedRoute is { } id ? await store.TraversalsAsync(id) : null);
        if (traversals is null) return [];
        try
        {
            return await turns.OfAsync(traversals);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.PlanningFailed(_logger, e);
            return [];
        }
    }

    // --- What the page reads and binds (T124c): the session's state, named as the page shows it. ---
    internal LatLon? Start => _start;
    internal FoundStart? Chosen => _chosen;
    // No more than 100 km (T153, #157: "should that even let you enter a value above 100km", user).
    internal double DistanceKm { get => _distanceKm; set => _distanceKm = Math.Min(value, WendYourWay.Core.Loops.Request.MaxDistanceKm); }
    internal double? FinishDistanceKm { get => _finishDistanceKm; set => _finishDistanceKm = value is { } km ? Math.Min(km, WendYourWay.Core.Loops.Request.MaxDistanceKm) : null; }
    internal double HillsLean { get => _hillsLean; set => _hillsLean = value; }
    internal double OffRoadLean { get => _offRoadLean; set => _offRoadLean = value; }
    internal Activity Activity => _activity;
    internal bool EndElsewhere => _endElsewhere;
    internal LatLon? Finish => _finish;
    internal bool FillFinish { get => _fillFinish; set => _fillFinish = value; }
    internal IReadOnlyList<PlannedLoop> Loops => _loops;
    internal int Current => _current;
    internal IOfferedLoops? Offered => _offered;
    internal bool Finding => _finding;
    internal PlanningProgress? Progress => _progress;
    internal bool Searching => _searching;
    internal bool NothingFound => _nothingFound;
    internal string? Problem => _problem;
    internal string? NudgeNote => _nudgeNote;
    internal bool OutingShown => _outingShown;
    internal bool PhoneAsked { get => _phoneAsked; set => _phoneAsked = value; }
    internal WendYourWay.Web.Phone.PhoneLink? Phone => _phone;
    internal bool RouteHidden => _routeHidden;
    internal int Saves => _saves;
    internal MapBounds? View => _starts.View;
    internal int Zoom => _starts.Zoom;
    internal DateTimeOffset? OsmDataAt => _osmDataAt;
    internal string ExportedAs => ExportName;

    /// <summary>What the Loop card shows (T124e).</summary>
    internal PagerView Pager => new(_loops, _current, _activity, _offered?.ShortestM, _finding, _progress, _searching, _nothingFound, _problem,
        _phoneAsked, _phone, _routeHidden, CanUndo, CanRedo, _nudgeNote, SuggestedName,
        // The car park started from, named on the card opened (T061).
        _chosen is { } chosen && _start == chosen.At ? chosen.Name : null);

    /// <summary>The Loop shown as a GPX file, its turns as waypoints, named as it's exported (T100, T112).</summary>
    internal async Task<GpxFile?> GpxAsync() => Shown is { } shown ? LoopGpx.Of(shown, await TurnsOfAsync(shown), ExportName) : null;

    /// <summary>The Loop shown as a TCX course, its turns as course points, for a watch (T100).</summary>
    internal async Task<GpxFile?> TcxAsync() => Shown is { } shown ? LoopTcx.Of(shown, await TurnsOfAsync(shown), ExportName) : null;

    public void Dispose()
    {
        _request?.Cancel();
        _request?.Dispose();
        _reRank?.Cancel();
        _reRank?.Dispose();
        _starts.Dispose();
    }
}
