using System.Text.Json;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;
using WendYourWay.Data.Building;
using WendYourWay.Data.UserData;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Backing up and restoring the user's own data (PLAN T102, STORY-031; user: "OneDrive",
/// "Automatically too"): the `app` schema by pg_dump, with a manifest of when, which migration and
/// each table's rows; restored into the tables as they are now, after a backup of what was there.
/// On the test database only, with an invented Run and invented rows.
/// </summary>
[TestFixture]
[NonParallelizable]
public class BackingUpTheUsersData
{
    private NpgsqlDataSource _dataSource = null!;
    private string _folder = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        await OutingImporter.ImportGpxAsync(_dataSource, Fixtures.Path("pavement-and-park.gpx"));
        await using var seed = _dataSource.CreateCommand("""
            update app.outings set user_stars = 4;
            insert into app.marks (osm_way_id, from_node_id, to_node_id, kind) values (200, 1, 2, 'blocked');
            insert into app.settings (last_request) values ('{"distanceKm": 10}'::jsonb);
            """);
        await seed.ExecuteNonQueryAsync();
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [SetUp]
    public void NewFolder() => _folder = Directory.CreateTempSubdirectory("cutthrough-backups-").FullName;

    [TearDown]
    public void NoFolder() => Directory.Delete(_folder, recursive: true);

    // Each app table's rows, as one checksum: the same rows, the same sum.
    private async Task<Dictionary<string, string>> Checksums()
    {
        var tables = new List<string>();
        await using (var list = _dataSource.CreateCommand("select table_name from information_schema.tables where table_schema = 'app' and table_type = 'BASE TABLE' order by 1"))
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        var sums = new Dictionary<string, string>();
        foreach (var table in tables)
        {
            await using var sum = _dataSource.CreateCommand($"select coalesce(md5(string_agg(t::text, '|' order by t::text)), '') from app.{table} t");
            sums[table] = (string)(await sum.ExecuteScalarAsync())!;
        }
        return sums;
    }

    //  T129: the jobs run (updates, imports) are the record of what was done, not the user's data:
    //  not in a backup, and a restore leaves them as they are, its own row among them.
    [Test]
    public async Task The_jobs_run_arent_backed_up_and_a_restore_leaves_them()
    {
        await using (var job = _dataSource.CreateCommand("delete from app.map_updates; insert into app.map_updates (kind, stage, source) values ('import', 'starting', 'before')"))
            await job.ExecuteNonQueryAsync();
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        await using (var job = _dataSource.CreateCommand("insert into app.map_updates (kind, stage, source) values ('restore', 'starting', 'after')"))
            await job.ExecuteNonQueryAsync();
        await UserDataBackups.RestoreAsync(TestDatabase.ConnectionString, backup.DumpPath, _folder);
        await using var sources = _dataSource.CreateCommand("select string_agg(source, ',' order by id) from app.map_updates");
        Assert.That((backup.Manifest.Rows.ContainsKey("map_updates"), await sources.ExecuteScalarAsync()), Is.EqualTo((false, (object)"before,after")));
    }

    //  T169: the API keys are kept out of the backups, and a restore leaves them as they are.
    [Test]
    public async Task The_API_keys_arent_backed_up_and_a_restore_leaves_them()
    {
        var keys = new ApiKeys(_dataSource);
        await keys.SaveAsync("OsMaps:ApiKey", "before-test-key");
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        await keys.SaveAsync("OsMaps:ApiKey", "after-test-key");
        await UserDataBackups.RestoreAsync(TestDatabase.ConnectionString, backup.DumpPath, _folder);
        var kept = await keys.AllAsync();
        await keys.DeleteAsync("OsMaps:ApiKey");
        Assert.That((backup.Manifest.Rows.ContainsKey("api_keys"), kept["OsMaps:ApiKey"]), Is.EqualTo((false, "after-test-key")));
    }

    //  Found at T132: the jobs' numbering was put back with the rest, so the next job's clashed.
    [Test]
    public async Task After_a_restore_the_next_job_has_a_number_of_its_own()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var jobs = new Jobs(_dataSource);
        var before = await jobs.StartAsync(JobKind.Restore, "before");
        await UserDataBackups.RestoreAsync(TestDatabase.ConnectionString, backup.DumpPath, _folder);
        Assert.That(await jobs.StartAsync(JobKind.Restore, "after"), Is.GreaterThan(before));
    }

    //  T138: the app now stops whenever its window closes, perhaps mid-backup. A backup is written
    //  under another name and renamed when whole, so one cut off never looks like a backup.
    [Test]
    public void One_that_fails_part_way_leaves_nothing_that_looks_like_a_backup()
    {
        var nowhere = new Npgsql.NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString) { Database = "cutthrough_none_test" }.ConnectionString;
        Assert.That(() => UserDataBackups.BackupAsync(nowhere, _folder), Throws.InstanceOf<InvalidOperationException>());
        Assert.That(Directory.EnumerateFiles(_folder), Is.Empty);
    }

    [Test]
    public async Task A_whole_one_leaves_no_part_behind()
    {
        await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        Assert.That(Directory.EnumerateFiles(_folder).Select(Path.GetExtension), Is.EquivalentTo(new[] { ".dump", ".json" }));
    }

    [Test]
    public async Task A_backup_is_a_dated_dump_of_the_users_data()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        Assert.That(Path.GetFileName(backup.DumpPath), Does.Match(@"^wend-your-way-\d{4}-\d{2}-\d{2}-\d{4}\.dump$"));
        Assert.That(new FileInfo(backup.DumpPath).Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task Its_manifest_says_which_migration_and_how_many_rows()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(Path.ChangeExtension(backup.DumpPath, ".json")), UserDataBackups.Json)!;
        Assert.That((manifest.Migration, manifest.Rows["outings"], manifest.Rows["marks"]), Is.EqualTo((Migrations.Latest, 1L, 1L)));
    }

    //  AC2: restored, every app table has the rows it had when the backup was taken.
    [Test]
    public async Task Restored_every_table_has_its_rows_again()
    {
        var before = await Checksums();
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        await using (var lose = _dataSource.CreateCommand("truncate app.outings, app.marks, app.settings, app.saved_routes restart identity cascade"))
            await lose.ExecuteNonQueryAsync();

        await UserDataBackups.RestoreAsync(TestDatabase.ConnectionString, backup.DumpPath, _folder);
        Assert.That(await Checksums(), Is.EqualTo(before));
    }

    [Test]
    public async Task A_restore_backs_up_what_was_there_first()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var report = await UserDataBackups.RestoreAsync(TestDatabase.ConnectionString, backup.DumpPath, _folder);
        Assert.That((File.Exists(report.Before.DumpPath), report.Before.DumpPath != backup.DumpPath), Is.EqualTo((true, true)));
    }

    [Test]
    public async Task A_restore_reports_each_tables_rows_against_the_backups()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var report = await UserDataBackups.RestoreAsync(TestDatabase.ConnectionString, backup.DumpPath, _folder);
        Assert.That(report.Rows.Values.All(r => r.Expected == r.Restored) && report.Rows["outings"].Restored == 1, Is.True);
    }

    [Test]
    public async Task A_backup_from_a_newer_WendYourWay_is_refused_and_nothing_changes()
    {
        var before = await Checksums();
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var manifestPath = Path.ChangeExtension(backup.DumpPath, ".json");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath), UserDataBackups.Json)!;
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest with { Migration = "CutThrough.Data.Migrations.9999_from_the_future.sql" }, UserDataBackups.Json));

        Assert.That(() => UserDataBackups.RestoreAsync(TestDatabase.ConnectionString, backup.DumpPath, _folder), Throws.TypeOf<BackupTooNewException>());
        Assert.That(await Checksums(), Is.EqualTo(before));
    }

    //  Kept (T134, user chose "Spread over a month"): the newest 3, then the newest of each of the
    //  4 latest days with none kept, then of each of the 3 latest weeks with none kept; nothing
    //  else in the folder touched.
    private void Backups(params string[] at)
    {
        foreach (var time in at)
        {
            File.WriteAllText(Path.Combine(_folder, $"wend-your-way-{time}.dump"), "");
            File.WriteAllText(Path.Combine(_folder, $"wend-your-way-{time}.json"), "{}");
        }
    }

    private string[] KeptAfterPruning()
    {
        UserDataBackups.Prune(_folder);
        return [.. Directory.EnumerateFiles(_folder, "*.dump").Select(f => Path.GetFileNameWithoutExtension(f)!["wend-your-way-".Length..]).Order(StringComparer.Ordinal)];
    }

    [Test]
    public void A_backup_a_day_keeps_the_last_week_and_a_week_before()
    {
        Backups([.. Enumerable.Range(1, 12).Select(d => $"2026-10-{d:00}-0800")]);
        //  12, 11, 10 the newest; 9 to 6 a day each; 4 for the week of 28 Sep (6 and 12 Oct's weeks have one).
        Assert.That(KeptAfterPruning(), Is.EqualTo(new[] { "2026-10-04-0800", "2026-10-06-0800", "2026-10-07-0800", "2026-10-08-0800",
            "2026-10-09-0800", "2026-10-10-0800", "2026-10-11-0800", "2026-10-12-0800" }));
    }

    [Test]
    public void A_busy_day_doesnt_push_out_last_month()
    {
        Backups([.. Enumerable.Range(8, 8).Select(h => $"2026-11-12-{h:00}00"),
            "2026-11-11-0900", "2026-11-10-0900", "2026-11-09-0900", "2026-11-08-0900", "2026-11-07-0900",
            "2026-11-01-0900", "2026-10-25-0900", "2026-10-18-0900", "2026-10-11-0900"]);
        Assert.That(KeptAfterPruning(), Is.EqualTo(new[] { "2026-10-18-0900", "2026-10-25-0900", "2026-11-01-0900",
            "2026-11-08-0900", "2026-11-09-0900", "2026-11-10-0900", "2026-11-11-0900",
            "2026-11-12-1300", "2026-11-12-1400", "2026-11-12-1500" }));
    }

    [Test]
    public void Its_manifests_go_with_them_and_nothing_else_is_touched()
    {
        Backups([.. Enumerable.Range(1, 12).Select(d => $"2026-10-{d:00}-0800")]);
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "mine");
        File.WriteAllText(Path.Combine(_folder, "wend-your-way-from-before.dump"), "");
        UserDataBackups.Prune(_folder);
        Assert.That((File.Exists(Path.Combine(_folder, "wend-your-way-2026-10-01-0800.json")), File.Exists(Path.Combine(_folder, "notes.txt")),
                File.Exists(Path.Combine(_folder, "wend-your-way-from-before.dump"))),
            Is.EqualTo((false, true, true)));
    }

    //  T132: the backups there for the Admin panel, newest first, each with when and its Outings.
    private void Backup(string time, long outings)
    {
        File.WriteAllText(Path.Combine(_folder, $"wend-your-way-{time}.dump"), "");
        var at = DateTimeOffset.ParseExact(time, "yyyy-MM-dd-HHmm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal);
        File.WriteAllText(Path.Combine(_folder, $"wend-your-way-{time}.json"),
            JsonSerializer.Serialize(new BackupManifest(at, Migrations.Latest, new Dictionary<string, long> { ["outings"] = outings }), UserDataBackups.Json));
    }

    [Test]
    public void The_backups_are_listed_newest_first_with_their_Outings()
    {
        Backup("2026-11-10-0900", 1_310);
        Backup("2026-11-12-1632", 1_316);
        Backup("2026-11-11-0900", 1_314);
        Assert.That(UserDataBackups.List(_folder).Select(b => (Path.GetFileName(b.DumpPath), b.Manifest.Rows["outings"])), Is.EqualTo(new[]
        {
            ("wend-your-way-2026-11-12-1632.dump", 1_316L), ("wend-your-way-2026-11-11-0900.dump", 1_314L), ("wend-your-way-2026-11-10-0900.dump", 1_310L),
        }));
    }

    [Test]
    public void Ten_at_most_are_listed()
    {
        foreach (var day in Enumerable.Range(1, 12)) Backup($"2026-10-{day:00}-0800", day);
        Assert.That(UserDataBackups.List(_folder).Select(b => b.Manifest.Rows["outings"]), Is.EqualTo(Enumerable.Range(3, 10).Reverse().Select(d => (long)d)));
    }

    [Test]
    public void One_without_a_manifest_that_reads_isnt_listed()
    {
        Backup("2026-11-12-1632", 1_316);
        File.WriteAllText(Path.Combine(_folder, "wend-your-way-2026-11-12-1700.dump"), "");
        File.WriteAllText(Path.Combine(_folder, "wend-your-way-2026-11-12-1800.dump"), "");
        File.WriteAllText(Path.Combine(_folder, "wend-your-way-2026-11-12-1800.json"), "not json");
        Assert.That(UserDataBackups.List(_folder).Select(b => Path.GetFileName(b.DumpPath)), Is.EqualTo(new[] { "wend-your-way-2026-11-12-1632.dump" }));
    }

    //  T140: backups named before the rename (cutthrough-app-…) are still listed, kept, found and due-checked.
    private void OldBackup(string time, long outings)
    {
        Backup(time, outings);
        foreach (var ext in new[] { ".dump", ".json" })
            File.Move(Path.Combine(_folder, $"wend-your-way-{time}{ext}"), Path.Combine(_folder, $"cutthrough-app-{time}{ext}"));
    }

    [Test]
    public void Backups_named_before_the_rename_are_still_listed()
    {
        OldBackup("2026-11-10-0900", 1_310);
        Backup("2026-11-12-1632", 1_316);
        Assert.That(UserDataBackups.List(_folder).Select(b => Path.GetFileName(b.DumpPath)),
            Is.EqualTo(new[] { "wend-your-way-2026-11-12-1632.dump", "cutthrough-app-2026-11-10-0900.dump" }));
    }

    [Test]
    public void One_named_before_is_found_by_its_name() =>
        Assert.That(() => { OldBackup("2026-11-10-0900", 1_310); return UserDataBackups.Find(_folder, "cutthrough-app-2026-11-10-0900.dump"); },
            Is.EqualTo(Path.Combine(_folder, "cutthrough-app-2026-11-10-0900.dump")));

    [Test]
    public void Pruning_counts_them_with_the_rest()
    {
        foreach (var day in Enumerable.Range(1, 6)) OldBackup($"2026-10-{day:00}-0800", day);
        foreach (var day in Enumerable.Range(7, 6)) Backup($"2026-10-{day:00}-0800", day);
        UserDataBackups.Prune(_folder);
        Assert.That(Directory.EnumerateFiles(_folder, "*.dump").Count(), Is.EqualTo(8));
    }

    [Test]
    public void One_named_before_counts_for_whether_one_is_due()
    {
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        File.WriteAllText(Path.Combine(_folder, $"cutthrough-app-{now.AddHours(-2).ToLocalTime():yyyy-MM-dd-HHmm}.dump"), "");
        Assert.That(UserDataBackups.IsDue(_folder, now), Is.False);
    }

    [Test]
    public void With_no_folder_none_are_listed() =>
        Assert.That(UserDataBackups.List(Path.Combine(_folder, "none")), Is.Empty);

    //  A backup to restore found by its name among the folder's own, never by a path.
    [Test]
    public void A_backup_is_found_by_its_name_among_the_folders_own()
    {
        Backup("2026-11-12-1632", 1_316);
        File.WriteAllText(Path.Combine(_folder, "wend-your-way-2026-11-12-1700.dump"), "");
        Assert.That((UserDataBackups.Find(_folder, "wend-your-way-2026-11-12-1632.dump"),
                UserDataBackups.Find(_folder, "wend-your-way-2026-11-12-1700.dump"),
                UserDataBackups.Find(_folder, Path.Combine("..", Path.GetFileName(_folder), "wend-your-way-2026-11-12-1632.dump")),
                UserDataBackups.Find(_folder, Path.Combine(_folder, "wend-your-way-2026-11-12-1632.dump")),
                UserDataBackups.Find(_folder, "wend-your-way-2026-11-13-0900.dump")),
            Is.EqualTo((Path.Combine(_folder, "wend-your-way-2026-11-12-1632.dump"), (string?)null, (string?)null, (string?)null, (string?)null)));
    }

    [TestCase(23, false)]
    [TestCase(25, true)]
    public void A_backup_is_due_when_the_newest_is_a_day_old(int hoursAgo, bool due)
    {
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var then = now.AddHours(-hoursAgo).ToLocalTime();
        File.WriteAllText(Path.Combine(_folder, $"wend-your-way-{then:yyyy-MM-dd-HHmm}.dump"), "");
        Assert.That(UserDataBackups.IsDue(_folder, now), Is.EqualTo(due));
    }

    [Test]
    public void With_no_backup_yet_one_is_due() =>
        Assert.That(UserDataBackups.IsDue(_folder, DateTimeOffset.Now), Is.True);

    [Test]
    public void The_folder_is_the_one_set_or_WendYourWay_Backups_in_OneDrive() =>
        Assert.That((UserDataBackups.FolderFor(@"D:\Mine", oneDrive: @"C:\OneDrive"), UserDataBackups.FolderFor(null, oneDrive: @"C:\OneDrive"), UserDataBackups.FolderFor(null, oneDrive: null)),
            Is.EqualTo((@"D:\Mine", Path.Combine(@"C:\OneDrive", "Wend Your Way Backups"), (string?)null)));

    //  The app's own database straight into the folder, another in its own folder, a test one never.
    [Test]
    public void Each_database_keeps_its_own_backups_and_a_test_one_none() =>
        Assert.That((UserDataBackups.FolderOf(@"C:\B", Database.DefaultConnectionString),
                UserDataBackups.FolderOf(@"C:\B", "Host=localhost;Database=cutthrough_london;Username=cutthrough"),
                UserDataBackups.FolderOf(@"C:\B", TestDatabase.ConnectionString),
                UserDataBackups.FolderOf(null, Database.DefaultConnectionString)),
            Is.EqualTo((@"C:\B", Path.Combine(@"C:\B", "cutthrough_london"), (string?)null, (string?)null)));

    [Test]
    public async Task An_automatic_backup_of_a_test_database_takes_none()
    {
        var said = new List<string>();
        Assert.That((await UserDataBackups.AutomaticallyAsync(TestDatabase.ConnectionString, _folder, said.Add), Directory.EnumerateFiles(_folder).Count()),
            Is.EqualTo(((UserDataBackup?)null, 0)));
    }
}
