using Bunit;
using WendYourWay.Data.Building;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The Admin panel (PLAN T129, STORY-048; user: "in truth I'm not going to be running commands in
/// the terminal so we might want an admin panel"): how Wend Your Way is at a glance (the map, the
/// runs, the backups, the disk) and the jobs that keep it so, run from here.
/// </summary>
public class LookingAfterWendYourWay
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    private PlannerScreen _screen = null!;

    [SetUp]
    public void Arrange() => _screen = new PlannerScreen(new FakePlanner());

    [TearDown]
    public void Dispose() => _screen.Dispose();

    private IRenderedComponent<Planner> OpenAdmin(AdminStatus status)
    {
        _screen.Admin.Status = status;
        var page = _screen.Open();
        page.Find(".rail button[aria-label='Admin']").Click();
        page.WaitForElement(".admin");
        return page;
    }

    private static AdminStatus Status(int outings = 1_314, int unrated = 220, DateTimeOffset? lastImport = null, DateTimeOffset? lastBackup = null,
        double? freeGb = 320.4, Job? running = null, Job? lastImportJob = null) =>
        new(outings, unrated, lastImport, lastBackup, @"C:\Users\someone\OneDrive\Wend Your Way Backups", freeGb, @"C:\", running, lastImportJob);

    private static Job Import(string outcome, string? message, string stage = "reading 3 files") =>
        new(8, Noon, Noon, outcome == "running" ? null : Noon, stage, outcome, message, "Downloads", null, JobKind.Import);

    private static string Said(IRenderedComponent<Planner> page, string part) => page.Find($".admin .{part}").TextContent.Trim();

    [Test]
    public void It_has_a_place_on_the_rail() =>
        Assert.That(_screen.Open().FindAll(".rail button").Select(b => b.GetAttribute("aria-label")), Does.Contain("Admin"));

    [Test]
    public void It_says_how_many_Outings_and_how_many_the_user_hasnt_rated() =>
        Assert.That(Said(OpenAdmin(Status()), "runs"), Does.Contain("1,314 Outings, 220 not rated by you"));

    [Test]
    public void One_Outing_is_one() =>
        Assert.That(Said(OpenAdmin(Status(outings: 1, unrated: 1)), "runs"), Does.Contain("1 Outing, 1 not rated by you"));

    [Test]
    public void It_says_when_runs_were_last_imported() =>
        Assert.That(Said(OpenAdmin(Status(lastImport: Noon)), "runs"), Does.Contain("Last imported 12 Nov 2026"));

    [Test]
    public void Not_knowing_when_it_says_so() =>
        Assert.That(Said(OpenAdmin(Status()), "runs"), Does.Contain("Last imported: not known"));

    [Test]
    public void It_says_when_the_last_backup_was_and_where() =>
        Assert.That(Said(OpenAdmin(Status(lastBackup: Noon)), "backups"), Does.Contain("Last backed up 12 Nov 2026").And.Contain("Wend Your Way Backups"));

    [Test]
    public void With_no_backup_it_says_so() =>
        Assert.That(Said(OpenAdmin(Status()), "backups"), Does.Contain("No backup yet"));

    [Test]
    public void It_says_how_much_room_there_is() =>
        Assert.That(Said(OpenAdmin(Status()), "disk"), Does.Contain(@"320 GB free on C:\"));

    [Test]
    public void Short_of_room_for_an_update_it_says_so() =>
        Assert.That(Said(OpenAdmin(Status(freeGb: 25)), "disk"), Does.Contain("too little for a map update"));

    [Test]
    public void A_job_running_says_what_and_how_far()
    {
        var importing = new Job(7, Noon, Noon, null, "reading", "running", null, "Downloads", null, JobKind.Import);
        Assert.That(Said(OpenAdmin(Status(running: importing)), "running"), Does.Contain("Importing runs: reading"));
    }

    // T130, STORY-048: runs from the Downloads folder, recorded ones only, none twice.
    [Test]
    public void Runs_are_imported_from_Downloads_at_a_click()
    {
        var page = OpenAdmin(Status());
        page.Find(".admin button.import").Click();
        Assert.That(_screen.Admin.Started, Is.EqualTo(new[] { JobKind.Import }));
    }

    [Test]
    public void The_button_says_where_from() =>
        Assert.That(OpenAdmin(Status()).Find(".admin button.import").TextContent.Trim(), Is.EqualTo("Import runs from Downloads"));

    [Test]
    public void Once_started_it_says_the_import_is_running()
    {
        var page = OpenAdmin(Status());
        _screen.Admin.StatusAfterStart = Status(running: Import("running", null));
        page.Find(".admin button.import").Click();
        Assert.That(Said(page, "running"), Does.Contain("Importing runs: reading 3 files"));
    }

    //  Found by hand (T132): wend takes a few seconds to begin, so the panel kept looking for it.
    [Test]
    public void A_job_slow_to_begin_is_still_seen_running()
    {
        var page = OpenAdmin(Status());
        (_screen.Admin.StatusAfterStart, _screen.Admin.ShowsAfterLooks) = (Status(running: Import("running", null)), 3);
        page.Find(".admin button.import").Click();
        page.WaitForAssertion(() => Assert.That(Said(page, "running"), Does.Contain("Importing runs: reading 3 files")), TimeSpan.FromSeconds(10));
    }

    //  Found by hand (T131): one done before it was ever seen running ends the looking too.
    [Test]
    public void A_job_done_before_it_was_seen_running_frees_the_buttons()
    {
        var page = OpenAdmin(Status());
        _screen.Admin.StatusAfterStart = Status(lastImportJob: Import("finished", "Imported 1 new run."));
        page.Find(".admin button.import").Click();
        page.WaitForAssertion(() => Assert.That(page.Find(".admin button.import").HasAttribute("disabled"), Is.False), TimeSpan.FromSeconds(4));
    }

    //  Until it's seen, a second click would start a second wend (advisor, T132).
    [Test]
    public void Till_a_job_slow_to_begin_is_seen_none_can_start()
    {
        var page = OpenAdmin(Status());
        (_screen.Admin.StatusAfterStart, _screen.Admin.ShowsAfterLooks) = (Status(running: Import("running", null)), 3);
        page.Find(".admin button.import").Click();
        Assert.That((page.FindAll(".admin .running").Count, page.Find(".admin button.import").HasAttribute("disabled")), Is.EqualTo((0, true)));
    }

    [Test]
    public void While_a_job_runs_no_import_can_start() =>
        Assert.That(OpenAdmin(Status(running: Import("running", null))).Find(".admin button.import").HasAttribute("disabled"), Is.True);

    [Test]
    public void Refused_it_says_why()
    {
        var page = OpenAdmin(Status());
        _screen.Admin.Refusal = "wend isn't built.";
        page.Find(".admin button.import").Click();
        Assert.That(page.Find(".admin .runs .problem").TextContent, Is.EqualTo("wend isn't built."));
    }

    [Test]
    public void It_says_how_the_last_import_went() =>
        Assert.That(Said(OpenAdmin(Status(lastImportJob: Import("finished", "Imported 3 new runs. Skipped: 2 already in."))), "runs"),
            Does.Contain("Last import, 12 Nov 2026:").And.Contain("Imported 3 new runs.").And.Contain("Skipped: 2 already in."));

    // T133 (user: "also need a linebreak befre "No new" and before Skipped").
    [Test]
    public void What_came_in_and_what_was_skipped_are_lines_of_their_own() =>
        Assert.That(OpenAdmin(Status(lastImportJob: Import("finished", "No new runs to import. Skipped: 1 already in, 2 planned routes, not recordings.")))
                .FindAll(".admin .last-import .line").Select(l => l.TextContent),
            Is.EqualTo(new[] { "Last import, 12 Nov 2026:", "No new runs to import.", "Skipped: 1 already in, 2 planned routes, not recordings." }));

    // T134 (user: "we have duplicate information").
    [Test]
    public void With_a_last_import_when_runs_were_last_imported_isnt_said_again() =>
        Assert.That(Said(OpenAdmin(Status(lastImport: Noon, lastImportJob: Import("finished", "Imported 2 new runs."))), "runs"), Does.Not.Contain("Last imported"));

    [Test]
    public void A_failed_import_says_why() =>
        Assert.That(Said(OpenAdmin(Status(lastImportJob: Import("failed", @"There's no folder at D:\Downloads to import from."))), "runs"),
            Does.Contain("The last import failed: There's no folder"));

    [Test]
    public void An_import_that_stopped_part_way_says_so() =>
        Assert.That(Said(OpenAdmin(Status(lastImportJob: Import("running", null))), "runs"), Does.Contain("The last import stopped part-way, reading 3 files."));

    // T132, STORY-048: "Back up now", the last ten with when and their Outings, and restoring one,
    // asked first (what's there now is backed up first).
    private static readonly BackupListed[] Ten =
    [
        .. Enumerable.Range(0, 10).Select(i => new BackupListed($"wend-your-way-2026-11-{13 - i:00}-0900.dump", new DateTimeOffset(2026, 11, 13 - i, 9, 0, 0, TimeSpan.Zero), 1_316 - i)),
    ];

    private static Job Restore(string outcome, string? message) =>
        new(9, Noon, Noon, outcome == "running" ? null : Noon, "restoring", outcome, message, "wend-your-way-2026-11-12-0900.dump", null, JobKind.Restore);

    private IRenderedComponent<Planner> OpenBackups(Job? running = null, Job? lastRestore = null, string? folder = @"C:\Users\someone\OneDrive\Wend Your Way Backups") =>
        OpenAdmin(Status(running: running) with { Backups = Ten, LastRestore = lastRestore, BackupFolder = folder });

    [Test]
    public void Back_up_now_backs_up_at_a_click()
    {
        OpenBackups().Find(".admin button.backup").Click();
        Assert.That(_screen.Admin.BackedUp, Is.EqualTo(1));
    }

    [Test]
    public void Backed_up_it_says_where()
    {
        var page = OpenBackups();
        _screen.Admin.BackUpSays = @"Backed up to C:\Users\someone\OneDrive\Wend Your Way Backups\wend-your-way-2026-11-13-1000.dump";
        page.Find(".admin button.backup").Click();
        Assert.That(Said(page, "backups .backed-up"), Does.Contain("wend-your-way-2026-11-13-1000.dump"));
    }

    //  It takes no lock: the one button left on while a job runs.
    [Test]
    public void While_a_job_runs_back_up_now_stays_on() =>
        Assert.That(OpenBackups(running: Import("running", null)).Find(".admin button.backup").HasAttribute("disabled"), Is.False);

    [Test]
    public void With_no_backup_folder_back_up_now_is_off() =>
        Assert.That(OpenBackups(folder: null).Find(".admin button.backup").HasAttribute("disabled"), Is.True);

    [Test]
    public void The_last_ten_are_listed_with_when_and_their_Outings()
    {
        var rows = OpenBackups().FindAll(".admin .backups li .when").Select(r => r.TextContent.Trim()).ToList();
        Assert.That((rows.Count, rows[0], rows[9]), Is.EqualTo((10, "13 Nov 2026, 09:00 · 1,316 Outings", "4 Nov 2026, 09:00 · 1,307 Outings")));
    }

    [Test]
    public void Restoring_one_asks_first_and_starts_nothing()
    {
        var page = OpenBackups();
        page.FindAll(".admin .backups li button.restore")[1].Click();
        Assert.That((Said(page, "backups .confirm .question"), _screen.Admin.Started.Count),
            Is.EqualTo(("Put back your data as it was on 12 Nov 2026, 09:00: 1,315 Outings? What's there now is backed up first.", 0)));
    }

    [Test]
    public void Confirmed_it_restores_that_backup()
    {
        var page = OpenBackups();
        page.FindAll(".admin .backups li button.restore")[1].Click();
        page.Find(".admin .backups .confirm button.yes").Click();
        Assert.That(_screen.Admin.Restoring, Is.EqualTo(new[] { "wend-your-way-2026-11-12-0900.dump" }));
    }

    [Test]
    public void Cancelled_nothing_is_restored()
    {
        var page = OpenBackups();
        page.FindAll(".admin .backups li button.restore")[1].Click();
        page.Find(".admin .backups .confirm button.no").Click();
        Assert.That((page.FindAll(".admin .backups .confirm").Count, _screen.Admin.Restoring.Count), Is.EqualTo((0, 0)));
    }

    [Test]
    public void While_a_job_runs_no_restore_can_start() =>
        Assert.That(OpenBackups(running: Import("running", null)).FindAll(".admin .backups li button.restore").All(b => b.HasAttribute("disabled")), Is.True);

    [Test]
    public void A_restore_refused_says_why()
    {
        var page = OpenBackups();
        _screen.Admin.Refusal = "That backup isn't in the folder any more.";
        page.FindAll(".admin .backups li button.restore")[0].Click();
        page.Find(".admin .backups .confirm button.yes").Click();
        Assert.That(Said(page, "backups .problem"), Is.EqualTo("That backup isn't in the folder any more."));
    }

    [Test]
    public void It_says_how_the_last_restore_went() =>
        Assert.That(Said(OpenBackups(lastRestore: Restore("finished", "Put back your data as it was on 12 Nov 2026, 09:00: 1,315 Outings.")), "backups"),
            Does.Contain("Last restore, 12 Nov 2026: Put back your data as it was on 12 Nov 2026, 09:00: 1,315 Outings. Reload the page to see it everywhere."));

    [Test]
    public void A_failed_restore_says_why() =>
        Assert.That(Said(OpenBackups(lastRestore: Restore("failed", "Put back, but not as the backup held it: outings 1 of 5.")), "backups"),
            Does.Contain("The last restore failed: Put back, but not as the backup held it: outings 1 of 5."));

    [Test]
    public void A_restore_that_stopped_part_way_says_so() =>
        Assert.That(Said(OpenBackups(lastRestore: Restore("running", null)), "backups"), Does.Contain("The last restore stopped part-way, restoring."));

    [Test]
    public void The_restore_running_isnt_said_to_have_stopped()
    {
        var restoring = Restore("running", null);
        Assert.That(Said(OpenBackups(running: restoring, lastRestore: restoring), "backups"), Does.Not.Contain("stopped part-way"));
    }

    // #135 (T131, STORY-048): back to the map before, if a new one turns out bad; done again, forward.
    private static readonly DateTimeOffset October = new(2026, 10, 1, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset November = new(2026, 11, 11, 21, 22, 47, TimeSpan.Zero);

    private static Job Back(string outcome, string? message) =>
        new(10, Noon, Noon, outcome == "running" ? null : Noon, "swapping", outcome, message, "network_prev", null, JobKind.Back);

    private IRenderedComponent<Planner> OpenTheMap(MapBefore? before, Job? running = null, Job? lastBack = null) =>
        OpenAdmin(Status(running: running) with { MapBefore = before, LastBack = lastBack });

    private static string? BackButton(IRenderedComponent<Planner> page) =>
        page.FindAll(".admin .the-map button.back").SingleOrDefault()?.TextContent.Trim();

    [Test]
    public void With_no_map_before_there_is_no_going_back() => Assert.That(BackButton(OpenTheMap(null)), Is.Null);

    [Test]
    public void It_offers_the_map_before_by_its_date() =>
        Assert.That(BackButton(OpenTheMap(new MapBefore(October, November, Fits: true))), Is.EqualTo("Go back to the map of 1 Oct 2026"));

    //  T150 (#154): a rebuild from the same extract keeps a map of the same paths' date, so each is
    //  named by when it was built.
    [Test]
    public void It_names_the_map_before_by_when_it_was_built() =>
        Assert.That(BackButton(OpenTheMap(new MapBefore(November, November, Fits: true, BuiltAt: November.AddDays(5)))), Is.EqualTo("Go back to the map built 16 Nov 2026"));

    [Test]
    public void Gone_back_from_a_rebuild_it_offers_forward_to_the_later_built() =>
        Assert.That(BackButton(OpenTheMap(new MapBefore(November, November, Fits: true, BuiltAt: November.AddDays(8), InUseBuiltAt: November.AddDays(5)))),
            Is.EqualTo("Go forward to the map built 19 Nov 2026"));

    [Test]
    public void One_with_no_date_is_the_map_before_this_one() =>
        Assert.That(BackButton(OpenTheMap(new MapBefore(null, November, Fits: true))), Is.EqualTo("Go back to the map before this one"));

    [Test]
    public void Gone_back_it_offers_forward() =>
        Assert.That(BackButton(OpenTheMap(new MapBefore(November, null, Fits: true))), Is.EqualTo("Go forward to the map of 11 Nov 2026"));

    [Test]
    public void One_of_an_older_make_is_said_and_not_offered()
    {
        var page = OpenTheMap(new MapBefore(October, November, Fits: false));
        Assert.That((BackButton(page), Said(page, "the-map").Contains("The map before this one was made by an older Wend Your Way, so it can't be gone back to.", StringComparison.Ordinal)),
            Is.EqualTo(((string?)null, true)));
    }

    [Test]
    public void Going_back_asks_first_and_starts_nothing()
    {
        var page = OpenTheMap(new MapBefore(October, November, Fits: true));
        page.Find(".admin .the-map button.back").Click();
        Assert.That((Said(page, "the-map .confirm .question"), _screen.Admin.Started.Count), Is.EqualTo((
            "Go back to the map of 1 Oct 2026? The map in use now is kept to come forward to, until the next map update replaces it. "
            + "Your Outings are covered and scored again on it, which takes a while.", 0)));
    }

    [Test]
    public void Going_forward_says_the_map_in_use_is_kept_to_go_back_to()
    {
        var page = OpenTheMap(new MapBefore(November, null, Fits: true));
        page.Find(".admin .the-map button.back").Click();
        Assert.That(Said(page, "the-map .confirm .question"), Does.StartWith("Go forward to the map of 11 Nov 2026? The map in use now is kept to go back to,"));
    }

    [Test]
    public void Confirmed_it_goes_back()
    {
        var page = OpenTheMap(new MapBefore(October, November, Fits: true));
        page.Find(".admin .the-map button.back").Click();
        page.Find(".admin .the-map .confirm button.yes").Click();
        Assert.That(_screen.Admin.Started, Is.EqualTo(new[] { JobKind.Back }));
    }

    [Test]
    public void Cancelled_it_doesnt()
    {
        var page = OpenTheMap(new MapBefore(October, November, Fits: true));
        page.Find(".admin .the-map button.back").Click();
        page.Find(".admin .the-map .confirm button.no").Click();
        Assert.That((page.FindAll(".admin .the-map .confirm").Count, _screen.Admin.Started.Count), Is.EqualTo((0, 0)));
    }

    [Test]
    public void While_a_job_runs_there_is_no_going_back() =>
        Assert.That(OpenTheMap(new MapBefore(October, November, Fits: true), running: Import("running", null)).Find(".admin .the-map button.back").HasAttribute("disabled"), Is.True);

    [Test]
    public void It_says_how_the_last_went() =>
        Assert.That(Said(OpenTheMap(new MapBefore(November, October, Fits: true), lastBack: Back("finished", "Gone back to the map of 1 Oct 2026.")), "the-map"),
            Does.Contain("Last change of map, 12 Nov 2026: Gone back to the map of 1 Oct 2026."));

    [Test]
    public void A_failed_one_says_why() =>
        Assert.That(Said(OpenTheMap(null, lastBack: Back("failed", "There's no map before this one to go back to.")), "the-map"),
            Does.Contain("Changing the map failed: There's no map before this one to go back to."));

    [Test]
    public void One_that_stopped_part_way_says_so() =>
        Assert.That(Said(OpenTheMap(null, lastBack: Back("running", null)), "the-map"), Does.Contain("Changing the map stopped part-way, swapping."));

    [Test]
    public void The_one_running_isnt_said_to_have_stopped()
    {
        var going = Back("running", null);
        Assert.That(Said(OpenTheMap(null, running: going, lastBack: going), "the-map"), Does.Not.Contain("stopped part-way"));
    }

    // Found by hand (T130): a section classed "map" took the full-screen map's style and covered the rest.
    [Test]
    public void Nothing_in_it_takes_the_maps_own_class() =>
        Assert.That(OpenAdmin(Status()).FindAll(".admin .map, .admin.map"), Is.Empty);

    [Test]
    public void The_maps_update_is_here_too() =>
        Assert.That(OpenAdmin(Status()).FindAll(".admin .map-update"), Has.Count.EqualTo(1));

    //  T171, #175: OS's place names and heights, brought up to date with the map, said by the release in use.
    private string OsData(OsDataInUse? data) => Said(OpenAdmin(Status() with { OsData = data }), "the-map .os-data");

    [Test]
    public void The_map_says_which_release_of_OSs_place_names_and_heights_is_in_use() =>
        Assert.That(OsData(new OsDataInUse("2026-10", "2026-07", HasHeights: true)),
            Is.EqualTo("Place names from OS Open Names 2026-10. Heights from OS Terrain 50 2026-07."));

    //  Loaded by hand before T171: the next map update fetches OS's newest.
    [Test]
    public void A_release_not_known_is_said_so() =>
        Assert.That(OsData(new OsDataInUse(null, null, HasHeights: true)),
            Is.EqualTo("Place names from OS Open Names, release not known. Heights from OS Terrain 50, release not known."));

    [Test]
    public void Without_heights_it_says_there_are_none() =>
        Assert.That(OsData(new OsDataInUse("2026-10", null, HasHeights: false)),
            Is.EqualTo("Place names from OS Open Names 2026-10. No heights: OS Terrain 50 isn't there, so nothing climbs."));

    [Test]
    public void Before_its_known_nothing_is_said() =>
        Assert.That(OpenAdmin(Status()).FindAll(".admin .os-data"), Is.Empty);
}
