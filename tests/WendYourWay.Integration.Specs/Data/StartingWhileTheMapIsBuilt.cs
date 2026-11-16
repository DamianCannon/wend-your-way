using Npgsql;
using WendYourWay.Data;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The app started while a map update, a build or another job runs (#153, T149; user: "we might want to
/// guard against it being opened when the map is updating then? ... shouldn't it be paused while this other
/// process is running?"): its database changes and its start-up backup held back till it next starts after.
/// </summary>
[TestFixture]
[NonParallelizable]
public class StartingWhileTheMapIsBuilt
{
    private const string Last = "0049_job_steps.sql";

    [OneTimeSetUp]
    public void Arrange() => Migrations.Apply(TestDatabase.ConnectionString);

    // A migration not yet applied, as an app newer than the database finds it: its journal row taken away.
    private static async Task<string> ForgetAsync()
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var forget = dataSource.CreateCommand($"delete from schemaversions where scriptname like '%{Last}' returning scriptname");
        return (string)(await forget.ExecuteScalarAsync())!;
    }

    private static async Task RememberAsync(string script)
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var remember = dataSource.CreateCommand("insert into schemaversions (scriptname, applied) select @script, now() where not exists (select 1 from schemaversions where scriptname = @script)");
        remember.Parameters.Add(new NpgsqlParameter("script", script));
        await remember.ExecuteNonQueryAsync();
    }

    private static async Task<bool> JournalledAsync(string script)
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var read = dataSource.CreateCommand("select exists (select 1 from schemaversions where scriptname = @script)");
        read.Parameters.Add(new NpgsqlParameter("script", script));
        return (bool)(await read.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task With_nothing_running_it_starts_as_it_always_has()
    {
        var started = await StartUp.PrepareAsync(TestDatabase.ConnectionString);
        Assert.That((started.JobRunning, started.HeldBack), Is.EqualTo((false, (IReadOnlyList<string>)[])));
    }

    [Test]
    public async Task With_a_build_running_its_database_changes_are_held_back()
    {
        var script = await ForgetAsync();
        try
        {
            StartUp.Prepared started;
            await using (await UpdateLock.TryTakeAsync(TestDatabase.ConnectionString))
                started = await StartUp.PrepareAsync(TestDatabase.ConnectionString);
            Assert.That((started.JobRunning, started.HeldBack, started.Migrated, await JournalledAsync(script)),
                Is.EqualTo((true, (IReadOnlyList<string>)[script], (IReadOnlyList<string>)[], false)));
        }
        finally
        {
            await RememberAsync(script);
        }
    }

    [Test]
    public async Task And_made_when_it_next_starts_after()
    {
        var script = await ForgetAsync();
        // Its column is there already: taken away too, so the migration can run again.
        await using (var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString))
        await using (var drop = dataSource.CreateCommand("alter table app.map_updates drop column step"))
            await drop.ExecuteNonQueryAsync();
        try
        {
            await using (await UpdateLock.TryTakeAsync(TestDatabase.ConnectionString))
                await StartUp.PrepareAsync(TestDatabase.ConnectionString);
            var started = await StartUp.PrepareAsync(TestDatabase.ConnectionString);
            Assert.That((started.JobRunning, started.Migrated, await JournalledAsync(script)), Is.EqualTo((false, (IReadOnlyList<string>)[script], true)));
        }
        finally
        {
            await RememberAsync(script);
        }
    }

    //  Looking doesn't take it: a build started just then isn't refused for it.
    [Test]
    public async Task Whether_one_is_running_is_seen_without_taking_its_lock()
    {
        Assert.That(await UpdateLock.IsHeldAsync(TestDatabase.ConnectionString), Is.False);
        await using var held = await UpdateLock.TryTakeAsync(TestDatabase.ConnectionString);
        Assert.That((held is not null, await UpdateLock.IsHeldAsync(TestDatabase.ConnectionString)), Is.EqualTo((true, true)));
    }
}
