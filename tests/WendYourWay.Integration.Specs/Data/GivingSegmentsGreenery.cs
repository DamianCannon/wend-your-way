using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Greenery (PLAN T036): the share of a Segment's length within 15 m of green areas, worked out as
/// the build inserts it. On Fixtures/greenery.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class GivingSegmentsGreenery
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("greenery.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<double> GreeneryOf(long wayId)
    {
        await using var query = _dataSource.CreateCommand("select greenery from network.segments where osm_way_id = @way");
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        return Convert.ToDouble(await query.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Test]
    public async Task Then_a_path_across_a_park_is_wholly_green() => Assert.That(await GreeneryOf(100), Is.EqualTo(1).Within(0.01));

    [Test]
    public async Task Then_a_path_with_nothing_green_near_it_isn_t() => Assert.That(await GreeneryOf(101), Is.Zero);

    [Test]
    public async Task Then_a_car_park_isn_t_green() => Assert.That(await GreeneryOf(102), Is.Zero);

    //  The planner reads it with the slice.
    [Test]
    public async Task Then_a_slice_carries_it()
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.328, 51.27), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new Core.Network.NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 2000);
        Assert.That(slice.Segments.Where(s => s.Greenery > 0.5).Select(s => s.Key.OsmWayId), Is.EqualTo(new long[] { 100 }));
    }
}
