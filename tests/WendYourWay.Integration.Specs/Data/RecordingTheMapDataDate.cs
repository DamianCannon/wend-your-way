using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The date of the map's data (PLAN T117; user: "we're seeing the current map tiles but *you* don't
/// see it - that's a bit annoying and confusing"): the extract's own date, as osm2pgsql records it,
/// kept with the Network the build makes. On Fixtures/same-ground.osm, its recorded dates set by hand.
/// </summary>
[TestFixture]
[NonParallelizable]
public class RecordingTheMapDataDate
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    // What osm2pgsql recorded of the extract, as a Geofabrik extract's import would: a value per
    // property, or none.
    private async Task Recorded(string? replication, string? import)
    {
        foreach (var (property, value) in new[] { ("replication_timestamp", replication), ("import_timestamp", import) })
        {
            await using var command = _dataSource.CreateCommand(value is null
                ? "delete from public.osm2pgsql_properties where property = @property"
                : "insert into public.osm2pgsql_properties (property, value) values (@property, @value) on conflict (property) do update set value = excluded.value");
            command.Parameters.Add(new NpgsqlParameter("property", property));
            if (value is not null) command.Parameters.Add(new NpgsqlParameter("value", value));
            await command.ExecuteNonQueryAsync();
        }
    }

    private Task<DateTimeOffset?> Shown() => new PostgisNetworkDate(_dataSource).OsmDataAtAsync();

    [Test]
    public async Task The_build_keeps_the_extracts_own_date()
    {
        await Recorded("2026-11-09T21:22:17Z", "2026-11-09T21:21:31Z");
        await NetworkBuild.Build(_dataSource);
        Assert.That(await Shown(), Is.EqualTo(new DateTimeOffset(2026, 11, 9, 21, 22, 17, TimeSpan.Zero)));
    }

    //  An extract with no replication header (an .osm file, say): its newest object's time.
    [Test]
    public async Task Without_the_extracts_date_its_newest_change()
    {
        await Recorded(null, "2026-10-01T08:00:00Z");
        await NetworkBuild.Build(_dataSource);
        Assert.That(await Shown(), Is.EqualTo(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public async Task With_neither_none_is_kept()
    {
        await Recorded(null, null);
        await NetworkBuild.Build(_dataSource);
        Assert.That(await Shown(), Is.Null);
    }

    //  The date is the Network's: a new extract imported but not yet built into one changes nothing.
    [Test]
    public async Task It_is_the_Networks_own_until_the_next_build()
    {
        await Recorded("2026-11-09T21:22:17Z", null);
        await NetworkBuild.Build(_dataSource);
        await Recorded("2026-11-11T21:00:00Z", null);
        Assert.That(await Shown(), Is.EqualTo(new DateTimeOffset(2026, 11, 9, 21, 22, 17, TimeSpan.Zero)));
    }

    [Test]
    public async Task Before_any_build_keeps_one_none_is_shown()
    {
        await using (var command = _dataSource.CreateCommand("delete from network.built_from"))
            await command.ExecuteNonQueryAsync();
        Assert.That(await Shown(), Is.Null);
    }
}
