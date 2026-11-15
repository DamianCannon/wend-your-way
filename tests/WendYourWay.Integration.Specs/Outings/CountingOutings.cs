using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Outings;
using Npgsql;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// The user's Outings at a glance, for the Admin panel (T129): how many, how many they haven't rated,
/// and when the last came in. On Fixtures/same-ground.osm with the invented Run of pavement-and-park.gpx.
/// </summary>
[TestFixture]
[NonParallelizable]
public class CountingOutings
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [SetUp]
    public async Task NoOutings()
    {
        await using var command = _dataSource.CreateCommand("delete from app.outings");
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task With_none_there_are_none_and_no_last_import() =>
        Assert.That(await OutingCounts.CountAsync(_dataSource), Is.EqualTo(new OutingCounts(0, 0, null)));

    [Test]
    public async Task One_imported_is_counted_unrated_and_dated_now()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        await OutingImporter.ImportGpxAsync(_dataSource, Fixtures.Path("pavement-and-park.gpx"));
        var counts = await OutingCounts.CountAsync(_dataSource);
        Assert.That((counts.Outings, counts.Unrated, counts.LastImported > before), Is.EqualTo((1, 1, true)));
    }

    [Test]
    public async Task Rated_it_isnt_unrated()
    {
        await OutingImporter.ImportGpxAsync(_dataSource, Fixtures.Path("pavement-and-park.gpx"));
        await using (var rate = _dataSource.CreateCommand("update app.outings set user_stars = 4"))
            await rate.ExecuteNonQueryAsync();
        Assert.That((await OutingCounts.CountAsync(_dataSource)).Unrated, Is.Zero);
    }
}
