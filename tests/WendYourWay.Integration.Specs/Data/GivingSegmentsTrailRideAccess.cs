using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// T021: each Segment's Trail Ride access and closed months, as the build gives them, matching the
/// Segments to the Definitive Map byways loaded from a rights-of-way folder (SPEC R9.6). On
/// Fixtures/trail-rides.osm, beside Fixtures/rights-of-way/WT/mutated4.json.
/// </summary>
[TestFixture]
[NonParallelizable]
public class GivingSegmentsTrailRideAccess
{
    private NpgsqlDataSource _dataSource = null!;
    private RightsOfWayBuild _loaded = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("trail-rides.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _loaded = (await NetworkBuild.Build(_dataSource, rightsOfWay: Fixtures.Path("rights-of-way"))).RightsOfWay!;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<(string Access, int ClosedMonths)> SegmentOf(long wayId)
    {
        await using var query = _dataSource.CreateCommand("select access_trail::text, closed_months from network.segments where osm_way_id = @way");
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        await using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, $"way {wayId} has no Segment");
        return (reader.GetString(0), reader.GetInt16(1));
    }

    [Test]
    public void Then_the_byways_are_loaded() =>
        Assert.That((_loaded.Authorities, _loaded.Byways), Is.EqualTo((1L, 3L)));

    //  An area whose Definitive Map isn't open: its boundary, from without-data/XX.kml (round way 408).
    [Test]
    public void Then_the_areas_without_a_Definitive_Map_are_loaded() =>
        Assert.That(_loaded.WithoutData, Is.EqualTo(1));

    [Test]
    public void Then_their_length_is_reported() =>
        Assert.That(_loaded.LengthM, Is.EqualTo(3 * 694).Within(10));

    [TestCase(400, "allowed", TestName = "Then_a_track_along_a_Definitive_Map_byway_may_be_ridden")]
    [TestCase(401, "forbidden", TestName = "Then_a_track_with_no_recorded_right_may_not")]
    [TestCase(402, "forbidden", TestName = "Then_a_footway_beside_a_byway_may_not")]
    [TestCase(403, "forbidden", TestName = "Then_OSMs_byway_on_no_Definitive_Map_line_may_not_even_in_a_neighbour_sharing_a_boundary_file")]
    [TestCase(404, "allowed", TestName = "Then_a_road_may_be_ridden")]
    [TestCase(405, "unconfirmed", TestName = "Then_a_green_lane_says_check_this")]
    [TestCase(407, "forbidden", TestName = "Then_a_track_across_a_byway_is_not_one")]
    [TestCase(408, "unconfirmed", TestName = "Then_OSMs_byway_where_no_Definitive_Map_is_open_says_check_this")]
    [TestCase(409, "forbidden", TestName = "Then_a_footpath_OSM_records_along_a_byway_stays_one")]
    public async Task Then_each_Segment_has_its_Trail_Ride_access(long wayId, string access) =>
        Assert.That((await SegmentOf(wayId)).Access, Is.EqualTo(access));

    [Test]
    public async Task Then_a_byway_closed_in_winter_keeps_its_months() =>
        Assert.That(await SegmentOf(406), Is.EqualTo(("allowed", 0b1110_0000_1111)));

    //  The byways loaded in place of those before, not beside them (building again changes no Segment).
    [Test]
    public async Task Then_a_build_again_loads_them_afresh()
    {
        await NetworkBuild.Build(_dataSource, rightsOfWay: Fixtures.Path("rights-of-way"));
        await using var count = _dataSource.CreateCommand("select (select count(*) from rights_of_way.byways), (select count(*) from rights_of_way.without_data)");
        await using var reader = await count.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.That((reader.GetInt64(0), reader.GetInt64(1)), Is.EqualTo((3L, 1L)));
    }

    // A Trail Ride's slice round the fixture, read on a day of the given month.
    private async Task<NetworkSlice> SliceIn(int month)
    {
        var clock = new OnDay(new DateTimeOffset(2026, month, 15, 9, 0, 0, TimeSpan.Zero));
        await using var middle = _dataSource.CreateCommand("select ST_X(geom), ST_Y(geom) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-1.795, 51.51), 4326), 27700) as geom) p");
        await using var reader = await middle.ExecuteReaderAsync();
        await reader.ReadAsync();
        return await new PostgisNetworkSlices(_dataSource, clock).AroundAsync(new NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 3000, Activity.TrailRide);
    }

    private sealed class OnDay(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static HashSet<long> Ways(NetworkSlice slice) => [.. slice.Segments.Select(s => s.Key.OsmWayId)];

    [Test]
    public async Task Then_a_Trail_Rides_slice_holds_what_a_trail_bike_may_use() =>
        Assert.That(Ways(await SliceIn(9)), Is.EquivalentTo(new long[] { 400, 404, 405, 406, 408 }));

    [Test]
    public async Task Then_a_byway_closed_in_winter_is_left_out_in_winter() =>
        Assert.That(Ways(await SliceIn(4)), Does.Not.Contain(406L));

    [Test]
    public async Task Then_its_Unconfirmed_ways_are_flagged() =>
        Assert.That((await SliceIn(7)).Segments.Where(s => s.Access == AccessKind.Unconfirmed).Select(s => s.Key.OsmWayId),
            Is.EquivalentTo(new long[] { 405, 408 }));

    [Test]
    public async Task Then_a_way_never_closed_has_none() =>
        Assert.That((await SegmentOf(400)).ClosedMonths, Is.Zero);
}
