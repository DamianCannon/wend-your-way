using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A route's turn points from its Segments (PLAN T100): its line and junctions read from the
/// Network, how many ways meet at each, and what it goes onto, by name or by kind. On
/// Fixtures/same-ground.osm: road 200 east from node 1 to node 2, side street 201 north from node 2
/// to node 4 (no name: "the road"), footway 220 parallel to the road.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FindingARoutesTurns
{
    private NpgsqlDataSource _dataSource = null!;
    private RouteTurns _turns = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _turns = new RouteTurns(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private static readonly (SegmentKey Key, bool Forward)[] EastThenNorth = [(new SegmentKey(200, 1, 2), true), (new SegmentKey(201, 2, 4), true)];

    [Test]
    public async Task East_along_the_road_then_north_up_the_side_street_is_a_left()
    {
        var turns = await _turns.OfAsync(EastThenNorth);
        Assert.That(turns.Select(t => (t.Kind, t.Words)), Is.EqualTo(new[] { (TurnKind.Left, "Left onto the road") }));
    }

    [Test]
    public async Task It_is_at_the_junction()
    {
        var at = (await _turns.OfAsync(EastThenNorth)).Single().At;
        Assert.That((at.Latitude, at.Longitude), Is.EqualTo((51.5550, -0.1630)).Using<(double, double)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 1e-5 && Math.Abs(a.Item2 - b.Item2) < 1e-5));
    }

    //  Back the way it came: north down 201 to node 2, then west along 200 to node 1, a right.
    [Test]
    public async Task The_other_way_round_is_a_right()
    {
        var turns = await _turns.OfAsync([(new SegmentKey(201, 2, 4), false), (new SegmentKey(200, 1, 2), false)]);
        Assert.That(turns.Select(t => t.Kind), Is.EqualTo(new[] { TurnKind.Right }));
    }

    [Test]
    public async Task Straight_on_along_the_road_says_nothing() =>
        Assert.That(await _turns.OfAsync([(new SegmentKey(200, 1, 2), true), (new SegmentKey(200, 2, 3), true)]), Is.Empty);

    [TestCase("footway", null, "the footpath")]
    [TestCase("path", null, "the path")]
    [TestCase("bridleway", null, "the bridleway")]
    [TestCase("cycleway", null, "the cycle path")]
    [TestCase("track", null, "the track")]
    [TestCase("steps", null, "the steps")]
    [TestCase("residential", null, "the road")]
    [TestCase("residential", "Bush Road", "Bush Road")]
    public void A_way_is_said_by_its_name_or_its_kind(string highway, string? name, string said) =>
        Assert.That(RouteTurns.Onto(highway, name), Is.EqualTo(said));
}
