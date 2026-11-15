using WendYourWay.Cli;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.UserData;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend backup [folder]` and `wend restore &lt;file&gt;` (PLAN T102, STORY-031), on the test database
/// with the invented Run of Fixtures/pavement-and-park.gpx; and the backups the commands that
/// change the user's data take by themselves, which a test database never does.
/// </summary>
[TestFixture]
[NonParallelizable]
public class BackingUpFromTheCommandLine
{
    private string _folder = null!;

    private static async Task<(int ExitCode, string Output)> Ct(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(args, output);
        return (exitCode, output.ToString());
    }

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        var build = await Ct("network", "build", Fixtures.Path("same-ground.osm"),
            "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"), "--osm2pgsql", Tools.Osm2pgsql);
        Assert.That(build.ExitCode, Is.Zero, build.Output);
        var import = await Ct("outings", "import-gpx", Fixtures.Path("pavement-and-park.gpx"), "--database", TestDatabase.ConnectionString);
        Assert.That(import.ExitCode, Is.Zero, import.Output);
        Assert.That(import.Output, Does.Not.Contain("Backed up"), "a test database is never backed up by itself");
    }

    [SetUp]
    public void NewFolder() => _folder = Directory.CreateTempSubdirectory("cutthrough-backups-").FullName;

    [TearDown]
    public void NoFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public async Task A_backup_says_where_it_went_and_its_rows()
    {
        var (exitCode, output) = await Ct("backup", _folder, "--database", TestDatabase.ConnectionString);
        Assert.That(exitCode, Is.Zero, output);
        Assert.That(output, Does.Contain(_folder).And.Match(@"outings\s+1\b"));
    }

    [Test]
    public async Task A_restore_puts_the_rows_back_and_says_so()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var (exitCode, output) = await Ct("restore", backup.DumpPath, "--database", TestDatabase.ConnectionString);
        Assert.That(exitCode, Is.Zero, output);
        Assert.That(output, Does.Match(@"outings\s+1\s+1\b").And.Not.Contain("differs"));
    }

    //  A test database's own rows, backed up before a restore, never land among the backups.
    [Test]
    public async Task What_a_test_database_had_first_is_kept_apart_from_the_backups()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var (exitCode, output) = await Ct("restore", backup.DumpPath, "--database", TestDatabase.ConnectionString);
        Assert.That((exitCode, Directory.EnumerateFiles(_folder, "*.dump").Count()), Is.EqualTo((0, 1)), output);
    }

    [Test]
    public async Task No_backup_there_is_refused()
    {
        var (exitCode, output) = await Ct("restore", Path.Combine(_folder, "none.dump"), "--database", TestDatabase.ConnectionString);
        Assert.That((exitCode, output.Contains("No backup there", StringComparison.Ordinal)), Is.EqualTo((1, true)));
    }

    [Test]
    public async Task A_backup_from_a_newer_WendYourWay_is_refused()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var manifest = Path.ChangeExtension(backup.DumpPath, ".json");
        await File.WriteAllTextAsync(manifest, (await File.ReadAllTextAsync(manifest)).Replace(Migrations.Latest, "CutThrough.Data.Migrations.9999_from_the_future.sql", StringComparison.Ordinal));
        var (exitCode, output) = await Ct("restore", backup.DumpPath, "--database", TestDatabase.ConnectionString);
        Assert.That((exitCode, output.Contains("newer Wend Your Way", StringComparison.Ordinal), (await LastJob()).Outcome), Is.EqualTo((1, true, "failed")));
    }

    //  T132: a restore is a job (the lock, its own row, how it went), as the Admin panel starts it.
    private static async Task<(string Kind, string Outcome, string? Message)> LastJob()
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var query = dataSource.CreateCommand("select kind, outcome, message from app.map_updates order by id desc limit 1");
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static async Task<long> Outings()
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var query = dataSource.CreateCommand("select count(*) from app.outings");
        return (long)(await query.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task A_restore_is_a_job_that_says_what_it_put_back()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var (exitCode, output) = await Ct("restore", backup.DumpPath, "--database", TestDatabase.ConnectionString);
        var (kind, outcome, message) = await LastJob();
        Assert.That((exitCode, kind, outcome), Is.EqualTo((0, JobKind.Restore, "finished")), output);
        Assert.That(message, Does.Match(@"^Put back your data as it was on \d{1,2} \w{3} \d{4}, \d{2}:\d{2}: 1 Outing\.$"));
    }

    [Test]
    public async Task Another_job_running_a_restore_waits_its_turn()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        await using (var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString))
        await using (var lose = dataSource.CreateCommand("truncate app.outings cascade"))
            await lose.ExecuteNonQueryAsync();
        try
        {
            await using var held = await UpdateLock.TryTakeAsync(TestDatabase.ConnectionString);
            var (exitCode, output) = await Ct("restore", backup.DumpPath, "--database", TestDatabase.ConnectionString);
            Assert.That((exitCode, output.Contains("already running", StringComparison.Ordinal), await Outings()), Is.EqualTo((1, true, 0L)), output);
        }
        finally
        {
            await Ct("restore", backup.DumpPath, "--database", TestDatabase.ConnectionString);
        }
    }

    [Test]
    public async Task Rows_not_as_the_backup_held_them_fail_it_naming_the_table()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var manifest = Path.ChangeExtension(backup.DumpPath, ".json");
        await File.WriteAllTextAsync(manifest, System.Text.RegularExpressions.Regex.Replace(await File.ReadAllTextAsync(manifest), @"""outings"": 1\b", @"""outings"": 5"));
        var (exitCode, _) = await Ct("restore", backup.DumpPath, "--database", TestDatabase.ConnectionString);
        var (_, outcome, message) = await LastJob();
        Assert.That((exitCode, outcome, message), Is.EqualTo((1, "failed", "Put back, but not as the backup held it: outings 1 of 5.")));
    }

    [Test]
    public async Task It_writes_to_the_log_given()
    {
        var backup = await UserDataBackups.BackupAsync(TestDatabase.ConnectionString, _folder);
        var log = Path.Combine(_folder, "restore.log");
        await Ct("restore", backup.DumpPath, "--database", TestDatabase.ConnectionString, "--log", log);
        Assert.That(await File.ReadAllTextAsync(log), Does.Contain("Put back your data"));
    }
}
