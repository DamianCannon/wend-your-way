using System.Text.Json;
using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// The Off-road networks as map tiles (PLAN T022b), on the real London Network: Richmond Park's
/// paths in the darkest band, Kenwood House's in the 30–100 km band, dots when zoomed out. Needs
/// `wend network offroad` run on London first.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class DrawingOffRoadNetworks
{
    private NpgsqlDataSource _dataSource = null!;
    private OffRoadTiles _tiles = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore(RealNetwork.London);
        _tiles = new OffRoadTiles(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    private async Task<int[]> BandsOfLines(int z, int x, int y)
    {
        using var tile = JsonDocument.Parse(await _tiles.JsonAsync(z, x, y));
        return [.. tile.RootElement.GetProperty("lines").EnumerateArray().Select(line => line[0].GetInt32())];
    }

    [Test]
    public async Task Then_Richmond_Parks_paths_are_in_the_largest_band() =>
        Assert.That((await BandsOfLines(14, 8179, 5450)).Max(), Is.EqualTo(4));

    [Test]
    public async Task Then_Kenwood_Houses_paths_are_in_the_30_to_100_km_band() =>
        Assert.That(await BandsOfLines(14, 8184, 5443), Does.Contain(2));

    [Test]
    public async Task Then_a_tile_is_served_within_half_a_second()
    {
        await _tiles.JsonAsync(14, 8184, 5443);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await _tiles.JsonAsync(14, 8183, 5443);
        Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(0.5)));
    }

    [Test]
    public async Task Then_zoomed_out_the_large_networks_are_dots()
    {
        using var tile = JsonDocument.Parse(await _tiles.JsonAsync(10, 511, 340));
        var dots = tile.RootElement.GetProperty("dots").EnumerateArray().ToList();
        Assert.That(dots.Select(d => d[2].GetDouble()), Is.Not.Empty.And.All.GreaterThanOrEqualTo(10));
    }

    [Test]
    public void Then_a_tile_that_doesn_t_exist_is_refused() =>
        Assert.That(() => _tiles.JsonAsync(3, 8, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
}
