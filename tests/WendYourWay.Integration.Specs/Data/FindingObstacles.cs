using WendYourWay.Core.Network;
using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Obstacles (PLAN T069): the barrier nodes and fords on each Segment, as the build finds them. On
/// Fixtures/obstacles.osm: a node inside a Segment is the Segment's; one on a junction is half each
/// Segment's that meets there; a ford is the whole way's; a bollard isn't one.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FindingObstacles
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("obstacles.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private static readonly (long Way, string Kind, double Share)[] Found =
    [
        (400, "gate", 1.5),
        (400, "kissing_gate", 1),
        (401, "gate", 0.5),
        (401, "stile", 1),
        (402, "ford", 1),
        (402, "gate", 1),
    ];

    [Test]
    public async Task Then_each_way_s_Segments_have_their_Obstacles()
    {
        await using var query = _dataSource.CreateCommand("""
            select s.osm_way_id, o.kind, sum(o.share)
              from network.segment_obstacles o join network.segments s on s.id = o.segment_id
             group by 1, 2 order by 1, 2
            """);
        var found = new List<(long, string, double)>();
        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync()) found.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDouble(2)));
        Assert.That(found, Is.EqualTo(Found));
    }

    //  A Gravel Ride's slice carries them; a Run's reads what it always did.
    [Test]
    public async Task Then_a_Gravel_Ride_s_slice_carries_them()
    {
        var slice = await SliceFor(Core.Loops.Activity.GravelRide);
        Assert.That(
            slice.Segments.SelectMany(s => (s.Obstacles ?? []).Select(o => (s.Key.OsmWayId, o.Kind, o.Share)))
                .GroupBy(o => (o.OsmWayId, o.Kind)).Select(g => (g.Key.OsmWayId, Name(g.Key.Kind), g.Sum(o => o.Share))).Order(),
            Is.EqualTo(Found));
    }

    [Test]
    public async Task Then_a_Run_s_slice_doesn_t() =>
        Assert.That((await SliceFor(Core.Loops.Activity.Run)).Segments.Where(s => s.Obstacles is not null), Is.Empty);

    private async Task<NetworkSlice> SliceFor(Core.Loops.Activity activity)
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.336, 51.225), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        return await new PostgisNetworkSlices(_dataSource).AroundAsync(new NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 2000, activity);
    }

    private static string Name(ObstacleKind kind) => kind switch
    {
        ObstacleKind.Gate => "gate",
        ObstacleKind.KissingGate => "kissing_gate",
        ObstacleKind.Stile => "stile",
        ObstacleKind.Ford => "ford",
        _ => kind.ToString(),
    };
}
