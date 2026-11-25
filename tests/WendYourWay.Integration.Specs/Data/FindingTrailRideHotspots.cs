using WendYourWay.Core.Loops;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Trail Ride hotspots (T189, #193; STORY-039's AC3): the off-road ways a Trail Ride may use, grouped where they lie
/// near one another (a motorbike may use every road, so joining through roads would join them all), kept with
/// their km, box and a junction near their middle. On Fixtures/gravel-networks.osm, its tracks made byways as the
/// Definitive Map would, with the spacing and the line for keeping them lowered to fit its small ground.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FindingTrailRideHotspots
{
    private NpgsqlDataSource _dataSource = null!;

    // Lowered from 2 km: 510 lies 1.1 km north of 500, so apart; 512 lies 670 m south, so near.
    private const double WithinM = 800;

    private NetworkBuildReport _built = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("gravel-networks.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _built = await NetworkBuild.Build(_dataSource);
        // Byways a Trail Ride may use, as the Definitive Map would say: 500, 502, 504 and 510; 512 unconfirmed,
        // and the footway 505 none.
        await using (var byways = _dataSource.CreateCommand("""
            update network.segments set access_trail = case when osm_way_id = 512 then 'unconfirmed' else 'allowed' end::access_kind
             where osm_way_id in (500, 502, 504, 510, 512);
            update network.segments set access_trail = 'forbidden' where osm_way_id = 505;
            insert into network.trailheads (osm_kind, osm_id, name, label, geom, network_id, network_m)
            values ('point', 1, 'Track car park', 'Track car park', ST_Transform(ST_SetSRID(ST_MakePoint(-0.3900, 51.2504), 4326), 27700), 1, 10000),
                   ('point', 2, 'Footway car park', 'Footway car park', ST_Transform(ST_SetSRID(ST_MakePoint(-0.3420, 51.2504), 4326), 27700), 1, 10000)
            """))
        {
            await byways.ExecuteNonQueryAsync();
        }
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await TrailRideHotspotsPhase.Build(connection, transaction, "network", CancellationToken.None, withinM: WithinM, keepFromM: 500);
        await transaction.CommitAsync();
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    // The km of off-road ground on the ways named, as the Network has them.
    private async Task<double> KmOf(params long[] ways)
    {
        await using var query = _dataSource.CreateCommand("select sum(length_m) from network.segments where osm_way_id = any(@ways) and ground = 'off_road'");
        query.Parameters.Add(new NpgsqlParameter("ways", ways));
        return Math.Round((double)(await query.ExecuteScalarAsync())! / 1000, 3);
    }

    private async Task<IReadOnlyList<Hotspot>> Kept(double fromKm = 0.5) =>
        await new NetworkHotspots(new PostgisGravelHotspots(_dataSource), _dataSource).OfAsync(Activity.TrailRide, fromKm);

    //  Byways near one another are one, whatever lies between; one further off is its own; the unconfirmed and the
    //  footway are in none, and the roads aren't counted.
    [Test]
    public async Task Then_byways_near_one_another_are_one_and_those_further_off_their_own() =>
        Assert.That((await Kept()).Select(h => Math.Round(h.Km, 3)), Is.EqualTo(new[] { await KmOf(500, 502, 504), await KmOf(510) }));

    //  Its box covers its byways; it's marked at a junction on them, near their middle.
    [Test]
    public async Task Then_each_is_marked_on_its_own_byways_within_its_box()
    {
        var largest = (await Kept())[0];
        Assert.That((Math.Round(largest.Box.West, 4), Math.Round(largest.Box.East, 4)), Is.EqualTo((-0.4000, -0.3470)));
        Assert.That(Math.Round(largest.At.Longitude, 4), Is.AnyOf(-0.3790, -0.3690));
    }

    //  A map build finds them too, in the Network it builds beside the one in use (none here: its tracks are made
    //  byways after it).
    [Test]
    public void Then_a_map_build_finds_them_too() =>
        Assert.That(_built.TrailRideHotspots, Is.EqualTo(new TrailRideHotspotBuild(0, 0)));

    //  Below the line asked for, none.
    [Test]
    public async Task Then_small_groups_aren_t_hotspots() =>
        Assert.That(await Kept(fromKm: 2), Has.Count.EqualTo(1));

    //  A Trail Ride's car parks are those beside its byways, with their km; no longer the Gravel networks'.
    private static readonly MapBounds AroundTheTracks = new(51.24, -0.41, 51.27, -0.33);

    [Test]
    public async Task Then_a_Trail_Ride_s_car_parks_are_those_beside_its_hotspots() =>
        Assert.That((await new PostgisCarParks(_dataSource).AroundAsync(AroundTheTracks, 10, Activity.TrailRide)).OnMap
            .Select(p => (p.Name, Math.Round(p.NetworkM / 1000, 3))),
            Is.EqualTo(new[] { ("Track car park", await KmOf(500, 502, 504)) }));
}

/// <summary>
/// Before a map is built with Trail Ride hotspots (T189), or gone back to one built before them: no Trail Ride
/// hotspots, and a Trail Ride's car parks still the Gravel networks', so they don't vanish until the next map update.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FindingTrailRideHotspotsOnAnOlderMap
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("gravel-networks.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        // As a map built before T189 is: its Gravel networks found, a Trail Ride's not.
        await using (var older = _dataSource.CreateCommand("""
            insert into network.trailheads (osm_kind, osm_id, name, label, geom, network_id, network_m)
            values ('point', 1, 'Track car park', 'Track car park', ST_Transform(ST_SetSRID(ST_MakePoint(-0.3900, 51.2504), 4326), 27700), 1, 10000);
            truncate network.trail_networks;
            update network.trailheads set trail_network_id = null, trail_network_m = null
            """))
        {
            await older.ExecuteNonQueryAsync();
        }
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await GravelNetworksPhase.Build(connection, transaction, "network", CancellationToken.None, keepFromM: 500);
        await transaction.CommitAsync();
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public async Task Then_there_are_no_Trail_Ride_hotspots() =>
        Assert.That(await new NetworkHotspots(new PostgisGravelHotspots(_dataSource), _dataSource).OfAsync(Activity.TrailRide, 0.5), Is.Empty);

    [Test]
    public async Task Then_a_Trail_Ride_s_car_parks_are_still_the_Gravel_networks() =>
        Assert.That((await new PostgisCarParks(_dataSource).AroundAsync(new MapBounds(51.24, -0.41, 51.27, -0.33), 10, Activity.TrailRide)).OnMap
            .Select(p => p.Name), Is.EqualTo(new[] { "Track car park" }));

    //  A map gone back to from before the table was made (T131) has none of it.
    [Test]
    public async Task Then_a_map_without_the_table_has_none_either()
    {
        await using (var gone = _dataSource.CreateCommand("alter table network.trail_networks rename to trail_networks_away"))
        {
            await gone.ExecuteNonQueryAsync();
        }
        try
        {
            Assert.That(await new NetworkHotspots(new PostgisGravelHotspots(_dataSource), _dataSource).OfAsync(Activity.TrailRide, 0.5), Is.Empty);
            Assert.That((await new PostgisCarParks(_dataSource).AroundAsync(new MapBounds(51.24, -0.41, 51.27, -0.33), 10, Activity.TrailRide)).OnMap
                .Select(p => p.Name), Is.EqualTo(new[] { "Track car park" }));
        }
        finally
        {
            await using var back = _dataSource.CreateCommand("alter table network.trail_networks_away rename to trail_networks");
            await back.ExecuteNonQueryAsync();
        }
    }
}
