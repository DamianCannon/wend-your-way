using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The planner's history (T078, T087; moved out of the page in T123): a step for each Request's
/// Loops and each Nudge, Undo and Redo through them, the last three Requests kept. Steps are
/// strings here; on the page, what's on screen.
/// </summary>
public class KeepingThePlannersHistory
{
    private PlannerHistory<string> _history = null!;

    [SetUp]
    public void Arrange() => _history = new PlannerHistory<string>();

    // A new Request's step, numbered as the page numbers them.
    private void Request(string step) => _history.Take(_history.NextRequest(), step);

    // A step within the Request on screen: a Nudge.
    private void Nudge(string step) => _history.Take(_history.CurrentRequest!.Value, step);

    [Test]
    public void Requests_are_numbered_one_after_another() =>
        Assert.That((_history.NextRequest(), _history.NextRequest(), _history.NextRequest()), Is.EqualTo((1, 2, 3)));

    [Test]
    public void With_nothing_taken_there_is_no_Request() => Assert.That(_history.CurrentRequest, Is.Null);

    [Test]
    public void One_step_can_t_be_undone() // nothing before it
    {
        Request("a");
        Assert.That((_history.CanUndo(finding: false), _history.CanRedo(finding: false)), Is.EqualTo((false, false)));
    }

    [Test]
    public void Undo_goes_back_a_step()
    {
        Request("a");
        Nudge("a nudged");
        Assert.That(_history.Undo(finding: false).Step, Is.EqualTo("a"));
    }

    [Test]
    public void Undo_within_a_Request_keeps_the_map_where_it_is()
    {
        Request("a");
        Nudge("a nudged");
        Assert.That(_history.Undo(finding: false).Fit, Is.False);
    }

    [Test]
    public void Undo_back_to_another_Request_fits_the_map_to_it()
    {
        Request("a");
        Request("b");
        Assert.That(_history.Undo(finding: false), Is.EqualTo(("a", true)));
    }

    [Test]
    public void Redo_goes_forward_again()
    {
        Request("a");
        Request("b");
        _history.Undo(finding: false);
        Assert.That((_history.CanRedo(finding: false), _history.Redo()), Is.EqualTo((true, ("b", true))));
    }

    [Test]
    public void Redo_within_a_Request_keeps_the_map_where_it_is()
    {
        Request("a");
        Nudge("a nudged");
        _history.Undo(finding: false);
        Assert.That(_history.Redo(), Is.EqualTo(("a nudged", false)));
    }

    [Test]
    public void Nothing_undone_there_is_nothing_to_redo()
    {
        Request("a");
        Request("b");
        Assert.That(_history.CanRedo(finding: false), Is.False);
    }

    [Test]
    public void A_step_taken_after_an_Undo_drops_what_could_be_redone()
    {
        Request("a");
        Request("b");
        _history.Undo(finding: false);
        Nudge("a nudged");
        Assert.That((_history.CanRedo(finding: false), _history.Undo(finding: false).Step), Is.EqualTo((false, "a")));
    }

    //  While a Request is found, Undo takes back that Request: the step on screen comes back, and the
    //  map is fitted to it, as it's another Request's.
    [Test]
    public void While_finding_one_step_can_be_undone()
    {
        Request("a");
        Assert.That(_history.CanUndo(finding: true), Is.True);
    }

    [Test]
    public void While_finding_Undo_brings_back_the_step_on_screen_fitted()
    {
        Request("a");
        Nudge("a nudged");
        Assert.That(_history.Undo(finding: true), Is.EqualTo(("a nudged", true)));
    }

    [Test]
    public void While_finding_Undo_stays_where_it_is_in_the_history()
    {
        Request("a");
        Nudge("a nudged");
        _history.Undo(finding: true);
        Assert.That(_history.Undo(finding: false).Step, Is.EqualTo("a"));
    }

    [Test]
    public void While_finding_with_nothing_taken_there_is_nothing_to_undo() =>
        Assert.That(_history.CanUndo(finding: true), Is.False);

    [Test]
    public void While_finding_there_is_nothing_to_redo()
    {
        Request("a");
        Request("b");
        _history.Undo(finding: false);
        Assert.That(_history.CanRedo(finding: true), Is.False);
    }

    //  The last three Requests' steps only: with a fourth, the first's steps, Nudges and all, go.
    [Test]
    public void Three_Requests_are_kept()
    {
        Request("1");
        Nudge("1 nudged");
        Request("2");
        Request("3");
        Request("4");
        var back = new List<string>();
        while (_history.CanUndo(finding: false)) back.Add(_history.Undo(finding: false).Step);
        Assert.That(back, Is.EqualTo(new[] { "3", "2" }));
    }

    [Test]
    public void Three_Requests_back_with_their_Nudges_are_all_there()
    {
        Request("1");
        Request("2");
        Nudge("2 nudged");
        Request("3");
        var back = new List<string>();
        while (_history.CanUndo(finding: false)) back.Add(_history.Undo(finding: false).Step);
        Assert.That(back, Is.EqualTo(new[] { "2 nudged", "2", "1" }));
    }

    //  The step on screen as it now is (the Loop paged to, the Loops re-ranked), in place.
    [Test]
    public void Amended_the_step_on_screen_is_as_it_now_is()
    {
        Request("a");
        Request("b");
        _history.Amend(finding: false, step => step + " paged");
        _history.Undo(finding: false);
        Assert.That(_history.Redo().Step, Is.EqualTo("b paged"));
    }

    [Test]
    public void While_finding_nothing_is_amended()
    {
        Request("a");
        Nudge("a nudged");
        _history.Amend(finding: true, step => step + " paged");
        Assert.That(_history.Undo(finding: true).Step, Is.EqualTo("a nudged"));
    }

    [Test]
    public void With_nothing_taken_nothing_is_amended()
    {
        _history.Amend(finding: false, step => step + " paged");
        Assert.That(_history.CurrentRequest, Is.Null);
    }

    [Test]
    public void The_Request_on_screen_is_the_one_undone_to()
    {
        Request("a");
        Request("b");
        _history.Undo(finding: false);
        Assert.That(_history.CurrentRequest, Is.EqualTo(1));
    }
}
