using WendYourWay.Data.Building;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// What the planner says of the map's update (T128), from the last run of `wend update`, the date of
/// Geofabrik's map, the date of the map in use and the time: running, stopped part-way, or how
/// the last one ended, and whether there's a newer map to update to.
/// </summary>
public class KnowingTheMapsUpdate
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 21, 22, 17, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);

    private static Job Run(string outcome, string stage = "building", TimeSpan? heardFrom = null, string? message = null) =>
        new(1, Now.AddHours(-2), Now - (heardFrom ?? TimeSpan.FromSeconds(30)), outcome == "running" ? null : Now.AddMinutes(-5), stage, outcome, message, ExtractSource.GreatBritain, Tuesday);

    //  T132: busy while a job of another kind runs (it holds the one lock); not once it has stopped.
    [Test]
    public void Another_kind_of_job_running_it_is_busy() =>
        Assert.That(MapUpdateState.Of(Run("finished"), Tuesday, Monday, Now, Run("running") with { Id = 2, Kind = JobKind.Import }).Busy, Is.True);

    [Test]
    public void Another_kind_of_job_stopped_it_isnt() =>
        Assert.That(MapUpdateState.Of(Run("finished"), Tuesday, Monday, Now, Run("running", heardFrom: TimeSpan.FromMinutes(6)) with { Id = 2, Kind = JobKind.Restore }).Busy, Is.False);

    [Test]
    public void Another_kind_of_job_ended_it_isnt() =>
        Assert.That(MapUpdateState.Of(Run("finished"), Tuesday, Monday, Now, Run("failed") with { Id = 2, Kind = JobKind.Import }).Busy, Is.False);

    [Test]
    public void The_update_itself_running_isnt_another() =>
        Assert.That(MapUpdateState.Of(Run("running"), Tuesday, Monday, Now, Run("running")).Busy, Is.False);

    [Test]
    public void Never_run_it_says_none() => Assert.That(MapUpdateState.Of(null, null, Monday, Now).Kind, Is.EqualTo(MapUpdateKind.None));

    [Test]
    public void Heard_from_lately_it_is_running() =>
        Assert.That(MapUpdateState.Of(Run("running"), Tuesday, Monday, Now).Kind, Is.EqualTo(MapUpdateKind.Running));

    //  Five minutes without a heartbeat: stopped part-way (killed, or the laptop off).
    [TestCase(4, MapUpdateKind.Running)]
    [TestCase(6, MapUpdateKind.Stopped)]
    public void Silent_for_a_while_it_has_stopped(int minutes, MapUpdateKind kind) =>
        Assert.That(MapUpdateState.Of(Run("running", heardFrom: TimeSpan.FromMinutes(minutes)), Tuesday, Monday, Now).Kind, Is.EqualTo(kind));

    [TestCase("finished", MapUpdateKind.Finished)]
    [TestCase("up to date", MapUpdateKind.UpToDate)]
    [TestCase("failed", MapUpdateKind.Failed)]
    public void Ended_it_says_how(string outcome, MapUpdateKind kind) => Assert.That(MapUpdateState.Of(Run(outcome), Tuesday, Monday, Now).Kind, Is.EqualTo(kind));

    [Test]
    public void It_keeps_the_stage_reached_and_the_sentence_why()
    {
        var state = MapUpdateState.Of(Run("failed", "downloading", message: "The download stopped part-way."), Tuesday, Monday, Now);
        Assert.That((state.Stage, state.Message, state.StartedAt), Is.EqualTo(("downloading", "The download stopped part-way.", (DateTimeOffset?)Now.AddHours(-2))));
    }

    [Test]
    public void Geofabriks_map_later_than_the_one_in_use_is_newer() => Assert.That(MapUpdateState.Of(null, Tuesday, Monday, Now).Newer, Is.True);

    [Test]
    public void Geofabriks_map_no_later_isnt() => Assert.That(MapUpdateState.Of(null, Monday, Monday, Now).Newer, Is.False);

    [Test]
    public void Geofabrik_not_asked_nothing_is_newer() => Assert.That(MapUpdateState.Of(null, null, Monday, Now).Newer, Is.False);

    [Test]
    public void With_no_date_for_the_map_in_use_Geofabriks_is_newer() => Assert.That(MapUpdateState.Of(null, Monday, null, Now).Newer, Is.True);
}
