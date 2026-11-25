using WendYourWay.Core.Loops;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Run hotspots (T190, #194; STORY-039): where off-road ground a Run may use is densest. The build keeps each square's
/// km; read, the squares holding enough are joined where they touch, into areas, each marked at a junction in its
/// densest square. On foot, Off-road networks join up across whole counties, so their size says little (T187). Laid
/// out here on Britain's grid in 1 km squares (5 km in a real build), two of 2 km or more needed to be dense:
/// <code>
///   y 151                   [C]                 C: 2.4 km, touching A2 at a corner only: an area of its own
///   x (km)   500  501  502  503  504  505
///            [A1] [A2] [s]            [B]       A1, A2: 2.4 km each, touching: one area of 4.8 km
///                                               s: 0.8 km, too little; B: 2.4 km, an area of its own
/// </code>
/// A road in A1 and a footpath a Run may not use in B are counted in none.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FindingRunHotspots
{
    private NpgsqlDataSource _dataSource = null!;
    private NetworkBuildReport _built = null!;

    private const double SquareM = 1000;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("gravel-networks.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _built = await NetworkBuild.Build(_dataSource);

        // In place of the fixture's, paths 800 m long, laid across each square at 200 m, 500 m and 800 m up.
        await using (var laid = _dataSource.CreateCommand("""
            delete from network.segments;
            insert into network.segments (osm_way_id, from_node_id, to_node_id, geom, ground, access_run)
            select row_number() over (), 1000 + row_number() over (), 2000 + row_number() over (),
                   ST_MakeLine(ST_MakePoint(x * 1000 + 100, 150000 + up), ST_MakePoint(x * 1000 + 900, 150000 + up))::geometry(LineString, 27700),
                   ground::ground_kind, access::access_kind
              from (values (500, 200, 'off_road', 'allowed'), (500, 500, 'off_road', 'allowed'), (500, 800, 'off_road', 'allowed'),
                           (500, 650, 'connector', 'allowed'),
                           (501, 200, 'off_road', 'allowed'), (501, 500, 'off_road', 'allowed'), (501, 800, 'off_road', 'allowed'),
                           (502, 500, 'off_road', 'allowed'),
                           (502, 1200, 'off_road', 'allowed'), (502, 1500, 'off_road', 'allowed'), (502, 1800, 'off_road', 'allowed'),
                           (505, 200, 'off_road', 'allowed'), (505, 500, 'off_road', 'allowed'), (505, 800, 'off_road', 'unconfirmed'),
                           (505, 650, 'off_road', 'forbidden')) as laid (x, up, ground, access)
            """))
        {
            await laid.ExecuteNonQueryAsync();
        }
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RunHotspotsPhase.Build(connection, transaction, "network", CancellationToken.None, squareM: SquareM);
        await transaction.CommitAsync();
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<IReadOnlyList<Hotspot>> Kept(double fromKm = 2) =>
        await new NetworkHotspots(new PostgisGravelHotspots(_dataSource), _dataSource).OfAsync(Activity.Run, fromKm);

    // A place on Britain's grid, in latitude and longitude.
    private async Task<LatLon> At(double x, double y)
    {
        await using var query = _dataSource.CreateCommand("select ST_Y(p), ST_X(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(@x, @y), 27700), 4326) p) t");
        query.Parameters.Add(new NpgsqlParameter("x", x));
        query.Parameters.Add(new NpgsqlParameter("y", y));
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new LatLon(reader.GetDouble(0), reader.GetDouble(1));
    }

    //  Dense squares side by side are one area, its km theirs together; one apart, or touching only at a corner, is
    //  its own (T190: joined at corners too, one area on the real map ran 45 km across); a square holding too
    //  little is in none; roads and paths a Run may not use aren't counted, the unconfirmed are.
    [Test]
    public async Task Then_dense_squares_that_touch_are_one_and_one_apart_its_own() =>
        Assert.That((await Kept()).Select(h => Math.Round(h.Km, 1)), Is.EqualTo(new[] { 4.8, 2.4, 2.4 }));

    //  Its box covers its squares; it's marked at a junction in it.
    [Test]
    public async Task Then_each_covers_its_squares_and_is_marked_within_them()
    {
        var largest = (await Kept())[0];
        var (southWest, northEast) = (await At(500_000, 150_000), await At(502_000, 151_000));
        Assert.That((Math.Round(largest.Box.West, 4), Math.Round(largest.Box.East, 4)), Is.EqualTo((Math.Round(southWest.Longitude, 4), Math.Round(northEast.Longitude, 4))));
        Assert.That(largest.At.Longitude, Is.InRange(largest.Box.West, largest.Box.East));
        Assert.That(largest.At.Latitude, Is.InRange(largest.Box.South, largest.Box.North));
    }

    //  The line for a dense square is read when they're shown, so it moves without a build: lowered, the sparse
    //  square joins the first area, and C above it; raised, none is dense.
    [TestCase(0.5, new[] { 8.0, 2.4 })]
    [TestCase(3.0, new double[0])]
    public async Task Then_the_line_for_a_dense_square_is_the_one_asked_for(double fromKm, double[] expected) =>
        Assert.That((await Kept(fromKm)).Select(h => Math.Round(h.Km, 1)), Is.EqualTo(expected));

    //  A map build finds the squares too, in the Network it builds beside the one in use.
    [Test]
    public void Then_a_map_build_finds_them_too() =>
        Assert.That(_built.RunHotspots?.Squares, Is.GreaterThan(0));
}
