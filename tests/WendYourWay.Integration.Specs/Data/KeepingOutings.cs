using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The user's own runs live in the `app` schema, apart from the Network, which is rebuilt
/// wholesale: an Outing and the Segments it covered survive every rebuild (PLAN T015, STORY-030,
/// ADR 0005). On Fixtures/topology.osm; the track is invented, on Hampstead Heath.
/// </summary>
[TestFixture]
[NonParallelizable]
public class KeepingOutings
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("topology.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    //  A Run along footway 10 (nodes 1 → 2 → 3), recorded from a GPX file.
    private async Task<int> ImportAnOuting()
    {
        await using var insert = _dataSource.CreateCommand("""
            insert into app.outings (kind, activity, name, source_file, track_hash, geom)
            values ('run', 'run', 'Morning run', 'morning.gpx', md5(random()::text),
                    ST_Transform(ST_GeomFromText('LINESTRING(-0.1650 51.5600, -0.1640 51.5600, -0.1630 51.5600)', 4326), 27700))
            returning id
            """);
        var id = (int)(await insert.ExecuteScalarAsync())!;

        await using var cover = _dataSource.CreateCommand("""
            insert into app.outing_coverage (outing_id, osm_way_id, from_node_id, to_node_id)
            values (@id, 10, 1, 2), (@id, 10, 2, 3)
            """);
        cover.Parameters.Add(new NpgsqlParameter("id", id));
        await cover.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<long> Count(string sql, int outing)
    {
        await using var query = _dataSource.CreateCommand(sql);
        query.Parameters.Add(new NpgsqlParameter("id", outing));
        return (long)(await query.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task Then_an_Outing_keeps_its_GPS_track_in_metres()
    {
        var outing = await ImportAnOuting();
        await using var query = _dataSource.CreateCommand("select length_m from app.outings where id = @id");
        query.Parameters.Add(new NpgsqlParameter("id", outing));
        Assert.That((double)(float)(await query.ExecuteScalarAsync())!, Is.EqualTo(138.4).Within(1.5));   // 0.002° of longitude
    }

    [Test]
    public async Task Then_a_Network_rebuild_leaves_Outings_and_what_they_covered()
    {
        var outing = await ImportAnOuting();
        await NetworkBuild.Build(_dataSource);
        Assert.Multiple(async () =>
        {
            Assert.That(await Count("select count(*) from app.outings where id = @id", outing), Is.EqualTo(1));
            Assert.That(await Count("select count(*) from app.outing_coverage where outing_id = @id", outing), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Then_an_Outing_s_coverage_goes_with_it()
    {
        var outing = await ImportAnOuting();
        await using (var delete = _dataSource.CreateCommand("delete from app.outings where id = @id"))
        {
            delete.Parameters.Add(new NpgsqlParameter("id", outing));
            await delete.ExecuteNonQueryAsync();
        }
        Assert.That(await Count("select count(*) from app.outing_coverage where outing_id = @id", outing), Is.Zero);
    }
}
