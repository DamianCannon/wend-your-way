using WendYourWay.Data;
using WendYourWay.Data.Building;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The practice map (T161, #165; user: "ok so now the demo map?"): Richmond Park and around, built
/// in a database of its own from the extract shipped with the app, its user data emptied each time
/// it opens. Here on the test database, the one other kind it may build in and empty.
/// </summary>
[TestFixture]
[NonParallelizable]
public class BuildingThePracticeMap
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        await PracticeMap.BuildAsync(TestDatabase.ConnectionString, Tools.Osm2pgsql, PracticeMap.Extract);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<long> Count(string table)
    {
        await using var count = _dataSource.CreateCommand($"select count(*) from {table}");
        return (long)(await count.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task It_s_built_from_the_extract_shipped_with_the_app() =>
        Assert.That((File.Exists(PracticeMap.Extract), await PracticeMap.IsBuiltAsync(TestDatabase.ConnectionString), await Count("network.segments") > 1000), Is.EqualTo((true, true, true)));

    [Test]
    public void Its_start_is_on_it() =>
        Assert.That(PracticeMap.Start, Is.EqualTo(new LatLon(51.4436, -0.2963)));

    //  Emptied as it opens: the Saved Routes, Marks and settings made there before; not its map.
    [Test]
    public async Task Opened_again_what_was_done_there_is_gone_but_not_its_map()
    {
        await using (var keep = _dataSource.CreateCommand("""
            insert into app.settings (last_request) values ('{"distanceKm": 12, "hillsLean": 0}')
            on conflict (id) do update set last_request = excluded.last_request
            """))
            await keep.ExecuteNonQueryAsync();
        var segments = await Count("network.segments");
        await PracticeMap.EmptyAsync(TestDatabase.ConnectionString);
        Assert.That((await Count("app.settings"), await Count("network.segments")), Is.EqualTo((0L, segments)));
    }

    [Test]
    public async Task An_empty_database_isn_t_built()
    {
        TestDatabase.Wipe();
        try
        {
            Assert.That(await PracticeMap.IsBuiltAsync(TestDatabase.ConnectionString), Is.False);
        }
        finally
        {
            await PracticeMap.BuildAsync(TestDatabase.ConnectionString, Tools.Osm2pgsql, PracticeMap.Extract);
        }
    }

    //  Beside the app's own database, never in it.
    [Test]
    public void Its_database_is_beside_the_app_s_own() =>
        Assert.That(new NpgsqlConnectionStringBuilder(PracticeMap.ConnectionStringOf("Host=localhost;Port=5432;Database=cutthrough;Username=cutthrough")).Database,
            Is.EqualTo("cutthrough_practice"));

    [Test]
    public void Given_its_own_already_it_s_the_same() =>
        Assert.That(new NpgsqlConnectionStringBuilder(PracticeMap.ConnectionStringOf("Host=localhost;Database=cutthrough_practice;Username=cutthrough")).Database,
            Is.EqualTo("cutthrough_practice"));

    //  Only a practice database, or a spec's, is ever emptied or built over. Named as no database is, so
    //  were the guard ever broken, nothing real could be touched.
    [TestCase("cutthrough_nowhere")]
    [TestCase("cutthrough_nowhere_london")]
    public void Any_other_database_is_refused(string database)
    {
        var other = $"Host=localhost;Port=5432;Database={database};Username=cutthrough";
        Assert.Multiple(() =>
        {
            Assert.That(() => PracticeMap.EmptyAsync(other), Throws.InvalidOperationException.With.Message.EqualTo(PracticeMap.NotPractice(database)));
            Assert.That(() => PracticeMap.BuildAsync(other, Tools.Osm2pgsql, PracticeMap.Extract), Throws.InvalidOperationException.With.Message.EqualTo(PracticeMap.NotPractice(database)));
        });
    }
}

/// <summary>What's done on the practice map is thrown away, so never backed up (T161).</summary>
public class NotBackingUpThePracticeMap
{
    [Test]
    public void Its_database_has_no_backup_folder() =>
        Assert.That(WendYourWay.Data.UserData.UserDataBackups.FolderOf(@"C:\Backups", "Host=localhost;Database=cutthrough_practice;Username=cutthrough"), Is.Null);
}
