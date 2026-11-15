using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// Car parks around here (PLAN T060): the Trailheads in view of a map over Richmond Park, on the real
/// London Network, at once. Needs `wend network offroad` run on London first.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class FindingCarParksAroundHere
{
    //  About what the map shows at zoom 13 over Richmond Park.
    private static readonly MapBounds RichmondPark = new(51.425, -0.32, 51.465, -0.23);

    private NpgsqlDataSource _dataSource = null!;
    private CarParksAround _around = null!;
    private TimeSpan _took;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.London);
        var carParks = new PostgisCarParks(_dataSource);
        await carParks.AroundAsync(RichmondPark, 10);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _around = await carParks.AroundAsync(RichmondPark, 10);
        _took = clock.Elapsed;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    [Test]
    public void Then_Richmond_Parks_gates_are_on_the_map() =>
        Assert.That(_around.OnMap.Select(c => c.Name), Has.Some.Contains("Gate"));

    [Test]
    public void Then_every_one_is_in_view() =>
        Assert.That(_around.OnMap, Has.All.Matches<FoundStart>(c =>
            c!.At.Latitude is >= 51.425 and <= 51.465 && c.At.Longitude is >= -0.32 and <= -0.23));

    [Test]
    public void Then_the_best_are_ten_at_most() =>
        Assert.That(_around.Best, Has.Count.InRange(1, 10).And.All.Matches<FoundStart>(c => _around.OnMap.Contains(c)));

    //  "It might be useful to include them all" (user, 2026-11-02): a network's places aren't one.
    [Test]
    public void Then_several_of_Richmond_Parks_gates_are_listed() =>
        Assert.That(_around.Best.Count(c => c.Name.Contains("Gate", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(3));

    //  "We just want one entry in the list" (user, 2026-11-02) for each place.
    [Test]
    public void Then_no_place_is_listed_twice() =>
        Assert.That(_around.Best.Select(c => c.Name).Where(name => name != "Car park"), Is.Unique);

    [Test]
    public void Then_the_best_has_the_most_green_ground() =>
        Assert.That(_around.Best[0].NetworkM, Is.EqualTo(_around.OnMap.Max(c => c.NetworkM)));

    //  T066: what the map says of each, kept by the build.
    [Test]
    public void Then_Pen_Ponds_car_park_is_free() =>
        Assert.That(_around.OnMap.Single(c => c.Name == "Pen Ponds Car Park").Details?.Fee, Is.EqualTo(WendYourWay.Core.Network.CarParkFee.Free));

    [Test]
    public void Then_some_have_toilets_or_a_cafe_near() =>
        Assert.That(_around.OnMap.Count(c => c.Details is { Toilets: true } or { Cafe: true }), Is.GreaterThan(0));

    [Test]
    public void Then_they_come_at_once() =>
        Assert.That(_took, Is.LessThan(TimeSpan.FromSeconds(0.2)));
}
