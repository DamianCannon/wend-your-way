using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Gravel hotspots (PLAN T072): Off-road ground a bike may use, joined through short roads as a
/// Run's Off-road networks are, kept with its km, box and a junction near its middle. On
/// Fixtures/gravel-networks.osm, with the line for keeping them lowered to fit its small networks.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FindingGravelHotspots
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
        // Two car parks (T073), as the Trailheads phase would keep them: one 50 m off track 500,
        // one beside the footway alone.
        await using (var parks = _dataSource.CreateCommand("""
            insert into network.trailheads (osm_kind, osm_id, name, label, geom, network_id, network_m)
            values ('point', 1, 'Track car park', 'Track car park', ST_Transform(ST_SetSRID(ST_MakePoint(-0.3900, 51.2504), 4326), 27700), 1, 10000),
                   ('point', 2, 'Footway car park', 'Footway car park', ST_Transform(ST_SetSRID(ST_MakePoint(-0.3420, 51.2504), 4326), 27700), 1, 10000)
            """))
        {
            await parks.ExecuteNonQueryAsync();
        }
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await GravelNetworksPhase.Build(connection, transaction, "network", CancellationToken.None, keepFromM: 500);
        await transaction.CommitAsync();
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    // The metres of track on the ways named, as the Network has them.
    private async Task<double> MetresOf(params long[] ways)
    {
        await using var query = _dataSource.CreateCommand("select sum(length_m) from network.segments where osm_way_id = any(@ways)");
        query.Parameters.Add(new NpgsqlParameter("ways", ways));
        return (double)(await query.ExecuteScalarAsync())!;
    }

    private async Task<IReadOnlyList<GravelHotspot>> Kept() => await new PostgisGravelHotspots(_dataSource).AllAsync(fromKm: 0.5);

    //  Tracks joined by a 70 m road, and by a 140 m one, a road link (T074), are one network; a
    //  track 1.1 km of road away, and one reached only by a Main Road, are each their own; the
    //  footway a bike may not use is in none, and the roads aren't counted.
    [Test]
    public async Task Then_road_links_join_tracks_and_long_or_main_road_ones_don_t()
    {
        var kept = (await Kept()).Select(h => Math.Round(h.Km, 3)).ToArray();
        var apart = new[] { Math.Round(await MetresOf(510) / 1000, 3), Math.Round(await MetresOf(512) / 1000, 3) }.OrderDescending();
        Assert.That(kept, Is.EqualTo(new[] { Math.Round(await MetresOf(500, 502, 504) / 1000, 3) }.Concat(apart)));
    }

    //  Its box covers its tracks; it's marked at a junction on them, near their middle.
    [Test]
    public async Task Then_each_is_marked_on_its_own_ground_within_its_box()
    {
        var largest = (await Kept())[0];
        Assert.That((Math.Round(largest.Box.West, 4), Math.Round(largest.Box.East, 4)), Is.EqualTo((-0.4000, -0.3470)));
        Assert.That(Math.Round(largest.At.Longitude, 4), Is.AnyOf(-0.3790, -0.3690));
    }

    //  T073: a Gravel Ride's car parks are those beside a Gravel Off-road network kept; a Run's,
    //  all of them.
    private static readonly MapBounds AroundTheTracks = new(51.24, -0.41, 51.26, -0.33);

    [Test]
    public async Task Then_a_Gravel_Ride_s_car_parks_are_those_beside_its_networks() =>
        Assert.That((await new PostgisCarParks(_dataSource).AroundAsync(AroundTheTracks, 10, Core.Loops.Activity.GravelRide)).OnMap
            .Select(p => (p.Name, Math.Round(p.NetworkM / 1000, 3))),
            Is.EqualTo(new[] { ("Track car park", Math.Round(await MetresOf(500, 502, 504) / 1000, 3)) }));

    [Test]
    public async Task Then_a_Run_s_are_all_of_them() =>
        Assert.That((await new PostgisCarParks(_dataSource).AroundAsync(AroundTheTracks, 10)).OnMap.Select(p => p.Name).Order(),
            Is.EqualTo(new[] { "Footway car park", "Track car park" }));

    //  Below the line asked for, none.
    [Test]
    public async Task Then_small_networks_aren_t_hotspots() =>
        Assert.That(await new PostgisGravelHotspots(_dataSource).AllAsync(fromKm: 2), Has.Count.EqualTo(1));
}
