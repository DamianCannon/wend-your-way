using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// PLAN T035: everything the plan needs from OpenStreetMap, kept in one change to the osm2pgsql
/// style, since each change costs a Great Britain re-import (ARCHITECTURE, "The osm2pgsql style,
/// designed in one go"). On Fixtures/kept-from-osm.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class KeepingWhatTheNetworkWillNeed
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("kept-from-osm.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<List<string>> Rows(string sql)
    {
        await using var query = _dataSource.CreateCommand(sql);
        await using var reader = await query.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        return rows;
    }

    //  A byway for Trail Ride (T020); a motorway for drive times (T024), never for a Run; and a
    //  pedestrian square drawn as an area, walked along its edge as other routers do (one of the
    //  user's rated Loops crossed one the Network didn't have, T032).
    [Test]
    public async Task Then_byways_motorways_and_squares_are_in_the_Network() =>
        Assert.That(await Rows("""
            select osm_way_id || ' ' || min(ground::text) || ' ' || min(access_run::text)
              from network.segments where osm_way_id in (101, 102, 103) group by osm_way_id order by osm_way_id
            """),
            Is.EqualTo(new[] { "101 traffic_free allowed", "102 off_road allowed", "103 connector forbidden" }));

    //  Green and open areas (Greenery, Open ground, opening hours) and car parks (Trailheads),
    //  from closed ways and multipolygon relations, in WGS84 like osm.ways: the Windows osm2pgsql
    //  can't project to British National Grid, so the build transforms them as it uses them.
    [Test]
    public async Task Then_parks_woods_and_car_parks_are_kept_as_areas() =>
        Assert.That(await Rows("""
            select coalesce(tags->>'leisure', tags->>'natural', tags->>'amenity', tags->>'highway', tags->>'landuse') || ' ' || (ST_Area(ST_Transform(geom, 27700)) > 1000)::text || ' ' || ST_SRID(geom)
              from osm.areas order by 1
            """),
            Is.EqualTo(new[] { "park true 4326", "parking true 4326", "pedestrian true 4326", "wood true 4326", "military true 4326" }.Order(StringComparer.Ordinal)));

    //  Obstacles, Crossings, Attractions and drinking water; not shops.
    [Test]
    public async Task Then_gates_crossings_viewpoints_and_water_are_kept_as_points() =>
        Assert.That(await Rows("""
            select coalesce(tags->>'barrier', tags->>'tourism', tags->>'amenity', tags->>'highway') from osm.points order by 1
            """),
            Is.EqualTo(new[] { "crossing", "drinking_water", "gate", "viewpoint" }));

    //  Corridors (T037): named walking and cycling routes, and their ways in order.
    [Test]
    public async Task Then_walking_routes_are_kept_with_their_ways_in_order() =>
        Assert.That(await Rows("""
            select r.tags->>'name' || ': ' || string_agg(w.way_id::text, ',' order by w.sequence)
              from osm.routes r join osm.route_ways w on w.relation_id = r.relation_id
             group by r.relation_id, r.tags
            """),
            Is.EqualTo(new[] { "The Test Ring: 100,102" }));

    //  T048: barriers, railways above ground and waterways, for Open ground and water; not a
    //  railway in a tunnel or a power line.
    [Test]
    public async Task Then_barriers_railways_and_waterways_are_kept_as_lines() =>
        Assert.That(await Rows("""
            select coalesce(tags->>'barrier', tags->>'railway', tags->>'waterway') || ' ' || ST_SRID(geom) from osm.lines order by 1
            """),
            Is.EqualTo(new[] { "fence 4326", "rail 4326", "river 4326", "stream 4326" }));

    //  T048: military land, which Open ground never crosses.
    [Test]
    public async Task Then_military_land_is_kept_as_an_area() =>
        Assert.That(await Rows("select count(*) from osm.areas where tags->>'landuse' = 'military'"), Is.EqualTo(new[] { "1" }));
}
