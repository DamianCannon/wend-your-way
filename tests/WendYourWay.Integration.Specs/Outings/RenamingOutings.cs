using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Integration.Specs.Support;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// Renaming an Outing (PLAN T111; user: "if I have a way to edit the name stored for an outing"):
/// the name kept, trimmed, and kept through a re-import of the same Strava export. On
/// Fixtures/same-ground.osm with the invented Strava archive.
/// </summary>
[TestFixture]
[NonParallelizable]
public class RenamingOutings
{
    private NpgsqlDataSource _dataSource = null!;
    private PostgisOutingViews _outings = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _outings = new PostgisOutingViews(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [SetUp]
    public async Task NoOutings()
    {
        await using var command = _dataSource.CreateCommand("delete from app.outings; delete from app.deleted_outings;");
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> IdOf(long stravaId)
    {
        await using var command = _dataSource.CreateCommand($"select id from app.outings where strava_id = {stravaId}");
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string?> NameOf(int id) => (await _outings.ListAsync()).SingleOrDefault(o => o.Id == id)?.Name;

    [Test]
    public async Task Renamed_it_is_listed_by_its_new_name_trimmed()
    {
        await StravaImporter.ImportAsync(_dataSource, SampleStravaArchive.Build());
        var id = await IdOf(101);
        await _outings.RenameAsync(id, "  Heath figure of eight ");
        Assert.That(await NameOf(id), Is.EqualTo("Heath figure of eight"));
    }

    [Test]
    public async Task The_same_Strava_export_imported_again_keeps_the_new_name()
    {
        var archive = SampleStravaArchive.Build();
        await StravaImporter.ImportAsync(_dataSource, archive);
        var id = await IdOf(101);
        await _outings.RenameAsync(id, "Heath figure of eight");
        await StravaImporter.ImportAsync(_dataSource, archive);
        Assert.That(await NameOf(id), Is.EqualTo("Heath figure of eight"));
    }

    [Test]
    public async Task A_blank_name_is_refused()
    {
        await StravaImporter.ImportAsync(_dataSource, SampleStravaArchive.Build());
        Assert.That(() => _outings.RenameAsync(IdOf(101).GetAwaiter().GetResult(), "  "), Throws.ArgumentException);
    }
}
