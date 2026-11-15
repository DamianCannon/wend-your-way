using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Blocked Marks (PLAN T082, SPEC R9.2): kept by a Segment's OSM identity in app.marks, and every
/// slice read treats a Blocked Segment as forbidden, for a Run and a Gravel Ride alike. On
/// Fixtures/obstacles.osm, three ways in a row near Box Hill.
/// </summary>
[TestFixture]
[NonParallelizable]
public class BlockingSegments
{
    private NpgsqlDataSource _dataSource = null!;
    private PostgisMarks _marks = null!;
    private SegmentKey _track;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("obstacles.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _marks = new PostgisMarks(_dataSource);
        await using var track = _dataSource.CreateCommand("select osm_way_id, from_node_id, to_node_id from network.segments where osm_way_id = 401");
        await using var reader = await track.ExecuteReaderAsync();
        await reader.ReadAsync();
        _track = new SegmentKey(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [TearDown]
    public async Task Unblock()
    {
        foreach (var mark in await _marks.ListAsync()) await _marks.RemoveAsync(mark.Id);
    }

    private async Task<AccessKind> AccessOf(SegmentKey key, Activity activity)
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.336, 51.225), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 2000, activity);
        return slice.Segments.SingleOrDefault(s => s.Key == key)?.Access ?? AccessKind.Forbidden;
    }

    [TestCase(Activity.Run)]
    [TestCase(Activity.GravelRide)]
    public async Task A_Blocked_Segment_is_forbidden_in_a_slice(Activity activity)
    {
        await _marks.BlockAsync(_track);
        Assert.That(await AccessOf(_track, activity), Is.EqualTo(AccessKind.Forbidden));
    }

    [Test]
    public async Task Unblocked_it_s_as_it_was()
    {
        var mark = await _marks.BlockAsync(_track);
        await _marks.RemoveAsync(mark);
        Assert.That(await AccessOf(_track, Activity.Run), Is.EqualTo(AccessKind.Allowed));
    }

    //  Listed with the line of its way, to draw it.
    [Test]
    public async Task A_Mark_is_listed_with_its_line()
    {
        await _marks.BlockAsync(_track);
        var listed = (await _marks.ListAsync()).Single();
        Assert.That((listed.Segment, listed.Line.Count >= 2, Math.Round(listed.Line[0].Latitude, 4)), Is.EqualTo((_track, true, 51.2250)));
    }

    //  Blocking it twice keeps one Mark.
    [Test]
    public async Task Blocking_it_again_keeps_one_Mark()
    {
        await _marks.BlockAsync(_track);
        await _marks.BlockAsync(_track);
        Assert.That(await _marks.ListAsync(), Has.Count.EqualTo(1));
    }
}
