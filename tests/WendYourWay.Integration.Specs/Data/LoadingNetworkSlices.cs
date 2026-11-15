using WendYourWay.Core.Network;
using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Loading the part of the Network a Request needs from PostGIS into memory (ADR 0006), on the
/// hand-written fixture (Fixtures/topology.osm).
/// </summary>
[TestFixture]
[NonParallelizable]
public class LoadingNetworkSlices
{
    private NpgsqlDataSource _dataSource = null!;
    private PostgisNetworkSlices _slices = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("topology.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _slices = new PostgisNetworkSlices(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    /// <summary>Node 2, where footway 10 crosses road 11, in British National Grid.</summary>
    private async Task<NodePoint> TheCrossing()
    {
        await using var query = _dataSource.CreateCommand(
            "select ST_X(ST_StartPoint(geom)), ST_Y(ST_StartPoint(geom)) from network.segments where osm_way_id = 10 and from_node_id = 2");
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new NodePoint(reader.GetDouble(0), reader.GetDouble(1));
    }

    [Test]
    public async Task Then_a_slice_holds_every_Segment_within_its_radius()
    {
        var slice = await _slices.AroundAsync(await TheCrossing(), radiusM: 1);
        Assert.That(slice.Segments.Select(s => s.Key), Is.EquivalentTo(new[]
        {
            new SegmentKey(10, 1, 2), new SegmentKey(10, 2, 3), new SegmentKey(11, 4, 2), new SegmentKey(11, 2, 5),
        }));
    }

    [Test]
    public async Task Then_each_Segment_keeps_its_ground_access_and_length()
    {
        var slice = await _slices.AroundAsync(await TheCrossing(), radiusM: 10_000);

        await using var query = _dataSource.CreateCommand(
            "select osm_way_id, from_node_id, to_node_id, ground::text, access_run::text, length_m from network.segments");
        await using var reader = await query.ExecuteReaderAsync();
        var stored = new List<string>();
        while (await reader.ReadAsync())
            stored.Add($"{reader.GetInt64(0)}:{reader.GetInt64(1)}-{reader.GetInt64(2)} {reader.GetString(3)} {reader.GetString(4)} {reader.GetDouble(5):R}");

        static string Snake(object kind) => string.Concat(kind.ToString()!.Select((c, i) => i > 0 && char.IsUpper(c) ? $"_{c}" : $"{c}")).ToLowerInvariant();
        Assert.That(
            slice.Segments.Select(s => $"{s.Key.OsmWayId}:{s.Key.FromNodeId}-{s.Key.ToNodeId} {Snake(s.Ground)} {Snake(s.Access)} {s.LengthM:R}"),
            Is.EquivalentTo(stored));
    }

    //  T068: a Gravel Ride's slice is read for a bike: footway 10 isn't in it at all.
    [Test]
    public async Task Then_a_Gravel_Rides_slice_leaves_out_what_a_bike_may_not_use()
    {
        var slice = await _slices.AroundAsync(await TheCrossing(), radiusM: 1, WendYourWay.Core.Loops.Activity.GravelRide);
        Assert.That(slice.Segments.Select(s => s.Key), Is.EquivalentTo(new[] { new SegmentKey(11, 4, 2), new SegmentKey(11, 2, 5) }));
    }

    [Test]
    public async Task Then_a_Gravel_Rides_slice_has_a_bikes_access()
    {
        var slice = await _slices.AroundAsync(await TheCrossing(), radiusM: 10_000, WendYourWay.Core.Loops.Activity.GravelRide);
        await using var query = _dataSource.CreateCommand("select count(*) from network.segments where access_ride <> 'forbidden'");
        Assert.That(slice.Segments.Count, Is.EqualTo((long)(await query.ExecuteScalarAsync())!));
    }

    //  A Network built before Gravel Rides has no bike access: say so, rather than guess.
    [Test]
    public async Task Then_a_Gravel_Ride_on_a_Network_built_before_them_asks_for_a_rebuild()
    {
        await using (var forget = _dataSource.CreateCommand("update network.segments set access_ride = null where osm_way_id = 11 and from_node_id = 4"))
            await forget.ExecuteNonQueryAsync();
        try
        {
            Assert.That(async () => await _slices.AroundAsync(await TheCrossing(), radiusM: 1, WendYourWay.Core.Loops.Activity.GravelRide),
                Throws.InstanceOf<NetworkNeedsRebuildException>());
        }
        finally
        {
            await using var restore = _dataSource.CreateCommand("update network.segments s set access_ride = ride_access(w.tags) from osm.ways w where w.way_id = s.osm_way_id and s.access_ride is null");
            await restore.ExecuteNonQueryAsync();
        }
    }

    [Test]
    public async Task Then_each_Segment_runs_between_the_junctions_at_its_ends()
    {
        var slice = await _slices.AroundAsync(await TheCrossing(), radiusM: 10_000);
        var crossing = await TheCrossing();
        var road = slice.Segments.Single(s => s.Key == new SegmentKey(11, 4, 2));
        Assert.That(slice.Node(road.To), Is.EqualTo(crossing));
    }
}
