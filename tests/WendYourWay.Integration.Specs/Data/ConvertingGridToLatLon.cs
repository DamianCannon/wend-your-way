using WendYourWay.Core.Network;
using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The app converts British National Grid to latitude and longitude itself, for the Loops a search
/// tries, drawn faintly as it goes (PLAN T054): within a few metres of PostGIS, from Cornwall to
/// Northumberland.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
public class ConvertingGridToLatLon
{
    private static readonly NodePoint[] Points =
    [
        new(530_000, 180_000),   // central London
        new(140_000, 30_000),    // Cornwall
        new(390_000, 600_000),   // Northumberland
        new(320_000, 270_000),   // the Welsh border
        new(650_000, 300_000),   // Norfolk
    ];

    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public async Task Arrange() => _dataSource = await RealNetwork.OpenOrIgnore();

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    [Test]
    public async Task Within_5_metres_of_PostGIS()
    {
        var expected = await new MapGeometry(_dataSource).ToLatLonAsync(Points);
        var apart = Points.Select((p, i) => MetresBetween(BritishGrid.ToLatLon(p), expected[i]));
        Assert.That(apart, Is.All.LessThan(5));
    }

    private static double MetresBetween(LatLon a, LatLon b)
    {
        var north = (a.Latitude - b.Latitude) * 111_200;
        var east = (a.Longitude - b.Longitude) * 111_200 * Math.Cos(a.Latitude * Math.PI / 180);
        return Math.Sqrt((north * north) + (east * east));
    }
}
