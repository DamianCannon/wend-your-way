using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A signed trail's name, code and links on pointing at it (T176, #180; user: "they have numbers and
/// codes on them which could do with a label or something useful to investigate them like a link"):
/// the routes whose ways pass near a point, from the map's own route relations. On Fixtures/corridors.osm,
/// where each way stands alone, about 280 m long and 550 m from the next.
/// </summary>
[TestFixture]
[NonParallelizable]
public class NamingSignedTrails
{
    private NpgsqlDataSource _dataSource = null!;
    private PostgisSignedRoutes _routes = null!;

    [OneTimeSetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("corridors.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _routes = new PostgisSignedRoutes(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    // Way 104, on the Downs Way (a national walking route) and the Abbey Walk (first by name, so national first is seen), about 10 m off it.
    private static readonly LatLon BesideTheDownsWay = new(51.27009, -0.3280);

    [Test]
    public async Task Then_the_routes_on_a_way_near_the_pointer_are_named_national_first() =>
        Assert.That((await _routes.NearAsync(BesideTheDownsWay, 25)).Select(r => r.Name), Is.EqualTo(new[] { "Downs Way", "Abbey Walk" }));

    [Test]
    public async Task Then_a_route_says_its_kind_and_network() =>
        Assert.That((await _routes.NearAsync(BesideTheDownsWay, 25)).Select(r => (r.Kind, r.Network)).First(), Is.EqualTo(("hiking", "nwn")));

    [Test]
    public async Task Then_a_route_with_a_website_gives_it() =>
        Assert.That((await _routes.NearAsync(BesideTheDownsWay, 25))[0].Website, Is.EqualTo("https://example.org/downs-way"));

    //  A website is OSM's word, linked on the card: only a web address is.
    [Test]
    public async Task Then_a_website_that_isn_t_a_web_address_is_left_out() =>
        Assert.That((await _routes.NearAsync(new LatLon(51.2600, -0.3280), 25)).Single().Website, Is.Null);

    [Test]
    public async Task Then_a_route_with_only_a_code_gives_its_code() =>
        Assert.That((await _routes.NearAsync(new LatLon(51.2650, -0.3280), 25)).Select(r => (r.Ref, r.Name)), Is.EqualTo(new[] { ("22", (string?)null) }));

    [Test]
    public async Task Then_a_walking_route_links_to_Waymarked_Trails_hiking_map_and_OpenStreetMap()
    {
        var downsWay = (await _routes.NearAsync(BesideTheDownsWay, 25))[0];
        Assert.That((downsWay.WaymarkedTrails, downsWay.OpenStreetMap), Is.EqualTo((
            new Uri("https://hiking.waymarkedtrails.org/#route?id=900"), new Uri("https://www.openstreetmap.org/relation/900"))));
    }

    [Test]
    public async Task Then_a_cycle_route_links_to_Waymarked_Trails_cycling_map() =>
        Assert.That((await _routes.NearAsync(new LatLon(51.2650, -0.3280), 25)).Single().WaymarkedTrails,
            Is.EqualTo(new Uri("https://cycling.waymarkedtrails.org/#route?id=903")));

    //  Halfway between ways 100 and 101: about 280 m from each.
    [Test]
    public async Task Then_away_from_any_route_there_are_none() =>
        Assert.That(await _routes.NearAsync(new LatLon(51.2525, -0.3280), 25), Is.Empty);

    //  The endpoint's guard: a crafted request can't read a whole county's routes.
    [Test]
    public async Task Then_no_further_than_200_m_is_searched() =>
        Assert.That(await _routes.NearAsync(new LatLon(51.2525, -0.3280), 5000), Is.Empty);
}
