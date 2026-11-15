using WendYourWay.Core.Network;
using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Turning Segments travelled in order into a line of latitudes and longitudes for the map and
/// GPX, and converting points between British National Grid and WGS84 (Fixtures/topology.osm).
/// </summary>
[TestFixture]
[NonParallelizable]
public class TracingLoopsForTheMap
{
    private NpgsqlDataSource _dataSource = null!;
    private MapGeometry _geometry = null!;

    //  Footway 10 runs west to east through nodes 1, 2 and 3, 0.001° of longitude apart.
    private static readonly LatLon Node1 = new(51.5600, -0.1650);
    private static readonly LatLon Node2 = new(51.5600, -0.1640);
    private static readonly LatLon Node3 = new(51.5600, -0.1630);

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("topology.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _geometry = new MapGeometry(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private static void AssertSameLine(IReadOnlyList<LatLon> actual, params LatLon[] expected)
    {
        Assert.That(actual, Has.Count.EqualTo(expected.Length));
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual[i].Latitude, Is.EqualTo(expected[i].Latitude).Within(1e-7), $"point {i}");
            Assert.That(actual[i].Longitude, Is.EqualTo(expected[i].Longitude).Within(1e-7), $"point {i}");
        }
    }

    [Test]
    public async Task Then_Segments_travelled_forwards_make_one_line_without_repeating_the_junction() =>
        AssertSameLine(
            await _geometry.TraceAsync([(new SegmentKey(10, 1, 2), true), (new SegmentKey(10, 2, 3), true)]),
            Node1, Node2, Node3);

    [Test]
    public async Task Then_Segments_travelled_backwards_are_traced_end_to_start() =>
        AssertSameLine(
            await _geometry.TraceAsync([(new SegmentKey(10, 2, 3), false), (new SegmentKey(10, 1, 2), false)]),
            Node3, Node2, Node1);

    //  A Segment the Network no longer has (it was rebuilt mid-Request, say) would leave a gap in
    //  the line, and in the GPX: tracing refuses rather than drawing it.
    [Test]
    public void Then_a_Segment_the_Network_does_not_have_is_refused() =>
        Assert.That(
            async () => await _geometry.TraceAsync([(new SegmentKey(10, 1, 2), true), (new SegmentKey(99_999, 1, 2), true)]),
            Throws.InstanceOf<InvalidOperationException>());

    [Test]
    public async Task Then_a_point_on_the_map_converts_to_the_grid_and_back()
    {
        var grid = await _geometry.ToGridAsync(Node2);
        var back = await _geometry.ToLatLonAsync([grid]);
        AssertSameLine(back, Node2);
    }

    [Test]
    public async Task Then_a_junction_on_the_grid_is_where_the_map_has_it()
    {
        await using var query = _dataSource.CreateCommand(
            "select ST_X(ST_StartPoint(geom)), ST_Y(ST_StartPoint(geom)) from network.segments where osm_way_id = 10 and from_node_id = 2");
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        AssertSameLine(await _geometry.ToLatLonAsync([new NodePoint(reader.GetDouble(0), reader.GetDouble(1))]), Node2);
    }
}
