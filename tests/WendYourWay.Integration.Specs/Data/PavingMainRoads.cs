using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Main Roads with a real pavement (T181, #185): 30 mph or under with pavements on both sides, or a pavement
/// mapped beside such a road, where a Run pays less a metre. On Fixtures/pavements.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PavingMainRoads
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

    private async Task<bool> HasPavement(long wayId)
    {
        await using var query = _dataSource.CreateCommand("select bool_and(pavement) from network.segments where osm_way_id = @way");
        query.Parameters.Add(new NpgsqlParameter("way", wayId));
        return (bool)(await query.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task Then_a_30_mph_Main_Road_with_its_pavements_mapped_has_one() => Assert.That(await HasPavement(100), Is.True);

    [Test]
    public async Task Then_its_pavement_mapped_beside_it_is_one() => Assert.That(await HasPavement(110), Is.True);

    [Test]
    public async Task Then_a_40_mph_Main_Road_hasn_t_whatever_its_pavements() => Assert.That(await HasPavement(200), Is.False);

    //  A quiet street with pavements isn't a Main Road: it costs a street's rate anyway.
    [Test]
    public async Task Then_a_quiet_street_isn_t_marked() => Assert.That(await HasPavement(101), Is.False);

    private async Task<long[]> PavedIn(Core.Loops.Activity activity)
    {
        await using var centre = _dataSource.CreateCommand("select ST_X(p), ST_Y(p) from (select ST_Transform(ST_SetSRID(ST_MakePoint(-0.302, 51.4585), 4326), 27700) p) t");
        await using var reader = await centre.ExecuteReaderAsync();
        await reader.ReadAsync();
        var slice = await new PostgisNetworkSlices(_dataSource).AroundAsync(new Core.Network.NodePoint(reader.GetDouble(0), reader.GetDouble(1)), 1000, activity);
        return [.. slice.Segments.Where(s => s.Pavement).Select(s => s.Key.OsmWayId).Distinct().Order()];
    }

    //  The planner reads it with a Run's slice; on a bike it's the road.
    [Test]
    public async Task Then_a_Run_s_slice_carries_it() =>
        Assert.That((await PavedIn(Core.Loops.Activity.Run)).Select(Math.Abs).Distinct().Order(), Is.EqualTo(new long[] { 100, 110 }));

    [Test]
    public async Task Then_a_Gravel_Ride_s_doesn_t() =>
        Assert.That(await PavedIn(Core.Loops.Activity.GravelRide), Is.Empty);
}
