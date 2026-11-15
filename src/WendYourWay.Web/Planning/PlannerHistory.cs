namespace WendYourWay.Web.Planning;

/// <summary>
/// The planner's history (T078; moved out of the page, T123): a step for each Request's Loops and
/// each Nudge, numbered by the Request it belongs to, through which Undo and Redo step. The last
/// <see cref="RequestsKept"/> Requests' steps only, their Loops still nudgeable. While a Request is
/// being found, Undo takes that Request back: the step on screen comes back, and nothing is redone.
/// </summary>
/// <typeparam name="TStep">What's on screen at a step; on the page, its Loops, start and finish.</typeparam>
public sealed class PlannerHistory<TStep>
{
    /// <summary>How many Requests' steps are kept.</summary>
    public const int RequestsKept = 3;

    private readonly List<(int Request, TStep Step)> _steps = [];
    private int _at = -1;
    private int _requests;

    /// <summary>The number of a new Request, one more than the last.</summary>
    public int NextRequest() => ++_requests;

    /// <summary>The Request the step on screen belongs to; none before any step is taken.</summary>
    public int? CurrentRequest => _at >= 0 ? _steps[_at].Request : null;

    /// <summary>
    /// What's on screen now, as a step of <paramref name="request"/>, after any that could be redone,
    /// which go; then only the last <see cref="RequestsKept"/> Requests' steps are kept.
    /// </summary>
    public void Take(int request, TStep step)
    {
        if (_at < _steps.Count - 1) _steps.RemoveRange(_at + 1, _steps.Count - _at - 1);
        _steps.Add((request, step));
        var kept = _steps.Select(s => s.Request).Distinct().TakeLast(RequestsKept).ToHashSet();
        _steps.RemoveAll(s => !kept.Contains(s.Request));
        _at = _steps.Count - 1;
    }

    /// <summary>The step on screen as it now is (the Loop paged to, the Loops re-ranked); not while finding.</summary>
    public void Amend(bool finding, Func<TStep, TStep> amend)
    {
        ArgumentNullException.ThrowIfNull(amend);
        if (_at >= 0 && !finding) _steps[_at] = (_steps[_at].Request, amend(_steps[_at].Step));
    }

    /// <summary>Whether there's a step to go back to; while finding, the one on screen.</summary>
    public bool CanUndo(bool finding) => finding ? _at >= 0 : _at > 0;

    /// <summary>Whether there's a step undone to go forward to; never while finding.</summary>
    public bool CanRedo(bool finding) => !finding && _at < _steps.Count - 1;

    /// <summary>
    /// A step back, and whether to fit the map to it: only if it's another Request's (T087). While
    /// finding, the step on screen, fitted, and the history where it was.
    /// </summary>
    public (TStep Step, bool Fit) Undo(bool finding)
    {
        if (!CanUndo(finding)) throw new InvalidOperationException("Nothing to undo.");
        var from = finding ? -1 : _steps[_at].Request;
        if (!finding) _at--;
        return (_steps[_at].Step, _steps[_at].Request != from);
    }

    /// <summary>A step forward again through what was undone, and whether to fit the map to it.</summary>
    public (TStep Step, bool Fit) Redo()
    {
        if (!CanRedo(finding: false)) throw new InvalidOperationException("Nothing to redo.");
        var from = _steps[_at].Request;
        _at++;
        return (_steps[_at].Step, _steps[_at].Request != from);
    }
}
