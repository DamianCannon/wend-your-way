using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Ways for customers only (T182, #186): kept in the Network for every Activity, and marked, to cost a
/// little more and be drawn striped. On Fixtures/pavements.osm, whose way 120 is a footway for customers.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MarkingWaysForCustomers
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("pavements.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<long[]> MarkedIn(Core.Loops.Activity activity)
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.302, 51.4600), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new Core.Network.NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 1000, activity);
        return [.. slice.Segments.Where(s => s.Customers).Select(s => s.Key.OsmWayId).Distinct().Order()];
    }

    [Test]
    public async Task Then_a_Run_s_slice_has_it_marked() => Assert.That(await MarkedIn(Core.Loops.Activity.Run), Is.EqualTo(new long[] { 120 }));

    //  Not a footpath to ride (no bicycle tag), so not in a ride's slice at all.
    [Test]
    public async Task Then_a_Gravel_Ride_s_slice_leaves_out_a_footway_as_ever() => Assert.That(await MarkedIn(Core.Loops.Activity.GravelRide), Is.Empty);
}
