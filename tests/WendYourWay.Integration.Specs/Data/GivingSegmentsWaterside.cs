using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Waterside (PLAN T043): the share of a Segment's length within 25 m of water, worked out as the
/// build inserts it. On Fixtures/waterside.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class GivingSegmentsWaterside
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("waterside.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<double> WatersideOf(long wayId)
    {
        await using var query = _dataSource.CreateCommand("select waterside from network.segments where osm_way_id = @way");
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        return Convert.ToDouble(await query.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Test]
    public async Task Then_a_towpath_is_wholly_beside_water() => Assert.That(await WatersideOf(100), Is.EqualTo(1).Within(0.01));

    [Test]
    public async Task Then_a_path_with_no_water_near_it_isn_t() => Assert.That(await WatersideOf(101), Is.Zero);

    [Test]
    public async Task Then_a_sewage_works_isn_t_water() => Assert.That(await WatersideOf(102), Is.Zero);

    [Test]
    public async Task Then_a_path_half_along_a_river_is_half_beside_water() => Assert.That(await WatersideOf(103), Is.EqualTo(0.5).Within(0.15));

    //  T048: a river mapped only as a line, as rural rivers and canals often are; not a stream.
    [Test]
    public async Task Then_a_path_beside_a_river_mapped_as_a_line_is_beside_water() => Assert.That(await WatersideOf(104), Is.EqualTo(1).Within(0.01));

    [Test]
    public async Task Then_a_path_beside_a_stream_isn_t() => Assert.That(await WatersideOf(105), Is.Zero);

    //  The planner reads it with the slice.
    [Test]
    public async Task Then_a_slice_carries_it()
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.328, 51.2475), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new Core.Network.NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 2000);
        Assert.That(slice.Segments.Where(s => s.Waterside > 0.9).Select(s => s.Key.OsmWayId).Order(), Is.EqualTo(new long[] { 100, 104 }));
    }
}
