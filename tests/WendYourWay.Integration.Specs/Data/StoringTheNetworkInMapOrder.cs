using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// A slice reads the Segments near its start, so the build stores them in map order: a slice of
/// Great Britain then reads a few neighbouring pages of the table instead of pages scattered
/// across all of it (T018: 936,000 pages for a 40 km slice in OSM way order, against 78,000 for
/// the same slice of London). On Fixtures/richmond-park.osm.gz.
/// </summary>
[TestFixture]
[NonParallelizable]
public class StoringTheNetworkInMapOrder
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("richmond-park.osm.gz"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    //  How many table pages the Segments within 500 m of the middle of the Network are spread
    //  over, against the fewest they'd fit in.
    private async Task<double> PagesReadPerPageNeeded(string table, string near)
    {
        await using var query = _dataSource.CreateCommand($"""
            with middle as (select ST_SetSRID(ST_Centroid(ST_Extent(geom)), 27700) as point from network.segments),
                 slice as (select s.id from network.segments s, middle m where ST_DWithin(s.geom, m.point, 500)),
                 rows as (select t.ctid from {table} t where {near} in (select id from slice)),
                 per_page as (select count(*)::float / (select relpages from pg_class where oid = '{table}'::regclass) as rows
                                from {table})
            select count(distinct (ctid::text::point)[0]) / ceil(count(*) / (select rows from per_page))
              from rows
            """);
        return (double)(await query.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task Then_a_slice_s_Segments_lie_on_neighbouring_pages() =>
        Assert.That(await PagesReadPerPageNeeded("network.segments", "t.id"), Is.LessThan(3));

    [Test]
    public async Task Then_their_same_ground_lies_on_neighbouring_pages_too() =>
        Assert.That(await PagesReadPerPageNeeded("network.segment_beside", "t.path_id"), Is.LessThan(3));
}
