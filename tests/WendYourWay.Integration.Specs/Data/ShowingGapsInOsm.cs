using WendYourWay.Core.Network;
using WendYourWay.Data;
using WendYourWay.Data.Building;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Gaps in OSM as a map overlay (T185, #189; user: "an overlay sounds worth doing as I can then go and check
/// manually to see what the problem happens to be"): every short gap the build found (T146a) in a view, with its
/// length and the two ways it nearly joins, to look at in OpenStreetMap. On Fixtures/gaps.osm, where 1: a
/// footway (way 111) ends 3 m short of a road (way 101).
/// </summary>
[TestFixture]
[NonParallelizable]
public class ShowingGapsInOsm
{
    private static readonly MapBounds Britain = new(49, -8, 61, 2);

    private NpgsqlDataSource _dataSource = null!;
    private IReadOnlyList<GapInOsm> _found = null!;
    private long _built;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("gaps.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        await using var count = _dataSource.CreateCommand("select count(*) from network.segments where gap is not null");
        _built = (long)(await count.ExecuteScalarAsync())!;
        _found = await new PostgisGapsInOsm(_dataSource).InViewAsync(Britain);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public void Then_every_gap_in_view_is_found() => Assert.That((_found.Count, _found.Count > 0), Is.EqualTo((_built, true)));

    [Test]
    public void Then_a_gap_says_how_long_it_is_and_whether_routing_crosses_it() =>
        Assert.That(_found.Where(g => g.PathWay == 111).Select(g => (g.Kind, Math.Round(g.LengthM))), Is.EqualTo(new[] { (MapGapKind.Joined, 3.0) }));

    [Test]
    public void Then_it_says_the_two_ways_it_nearly_joins() =>
        Assert.That(_found.Where(g => g.PathWay == 111).Select(g => (g.PathWay, g.OntoWay)), Is.EqualTo(new[] { (111L, 101L) }));

    [Test]
    public void Then_it_runs_from_the_paths_end_to_the_way_it_stops_short_of() =>
        Assert.That(_found.Where(g => g.PathWay == 111).Select(g => Math.Round(Distance(g.From, g.To))), Is.EqualTo(new[] { 3.0 }));

    [Test]
    public async Task Out_of_view_none_are_found() =>
        Assert.That(await new PostgisGapsInOsm(_dataSource).InViewAsync(new MapBounds(57, -5, 58, -4)), Is.Empty);

    // Metres between two points, near enough for a few.
    private static double Distance(LatLon a, LatLon b) =>
        Math.Sqrt(Math.Pow((a.Latitude - b.Latitude) * 111_200, 2) + Math.Pow((a.Longitude - b.Longitude) * 111_200 * Math.Cos(a.Latitude * Math.PI / 180), 2));
}
