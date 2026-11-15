using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// Cafés and toilets on the map (PLAN T067): those mapped as points in a view over Richmond Park, on
/// the real London map data, at once.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class FindingCafesAndToilets
{
    private static readonly MapBounds RichmondPark = new(51.425, -0.32, 51.465, -0.23);

    private NpgsqlDataSource _dataSource = null!;
    private IReadOnlyList<Amenity> _found = null!;
    private TimeSpan _took;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore(Environment.GetEnvironmentVariable("WEND_REAL_DATABASE") ?? RealNetwork.London);
        var amenities = new PostgisAmenities(_dataSource);
        await amenities.InViewAsync(RichmondPark);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _found = await amenities.InViewAsync(RichmondPark);
        _took = clock.Elapsed;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    [Test]
    public void Then_there_are_cafes_and_toilets() =>
        Assert.That(_found.Select(a => a.Kind).Distinct().Order(), Is.EqualTo(new[] { AmenityKind.Cafe, AmenityKind.Toilets }));

    [Test]
    public void Then_a_cafe_has_its_name() =>
        Assert.That(_found.Where(a => a.Kind == AmenityKind.Cafe).Select(a => a.Name), Has.Some.Contains("Pembroke Lodge"));

    [Test]
    public void Then_every_one_is_in_view() =>
        Assert.That(_found, Has.All.Matches<Amenity>(a => a is not null &&
            a.At.Latitude is >= 51.425 and <= 51.465 && a.At.Longitude is >= -0.32 and <= -0.23));

    [Test]
    public void Then_they_come_at_once() =>
        Assert.That(_took, Is.LessThan(TimeSpan.FromSeconds(0.1)));

    [Test]
    public async Task Then_no_more_than_500_come_for_a_crowded_view() =>
        Assert.That(await new PostgisAmenities(_dataSource).InViewAsync(new MapBounds(51.47, -0.30, 51.56, -0.05)), Has.Count.LessThanOrEqualTo(PostgisAmenities.MostInView));
}
