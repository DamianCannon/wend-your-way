using WendYourWay.Core.Network;
using WendYourWay.Data;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// Reading a slice says how far it has got (PLAN T054): the Segments read so far and, once counted
/// alongside, how many there are. On the real London Network, 3 km round Kenwood House.
/// </summary>
[TestFixture]
[Category("RealNetwork")]
[NonParallelizable]
public class ReadingASlice
{
    private static readonly NodePoint KenwoodHouse = new(527_100, 187_300);

    private NpgsqlDataSource _dataSource = null!;
    private readonly List<SliceReading> _seen = [];
    // The Segments in the Network within the slice: what's read, not the slice's own count, which a Run's
    // corners (#149, T145) add copies and crossings to.
    private int _read;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        _dataSource = await RealNetwork.OpenOrIgnore();
        await new PostgisNetworkSlices(_dataSource).AroundAsync(KenwoodHouse, 3000, reading: _seen.Add);
        await using var count = _dataSource.CreateCommand(
            "select count(*) from network.segments where ST_DWithin(geom, ST_SetSRID(ST_MakePoint(@x, @y), 27700), 3000)");
        count.Parameters.Add(new NpgsqlParameter("x", KenwoodHouse.X));
        count.Parameters.Add(new NpgsqlParameter("y", KenwoodHouse.Y));
        _read = Convert.ToInt32(await count.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    [Test]
    public void Then_it_reports_as_it_reads() =>
        Assert.That(_seen.Count(r => r.Read < _read), Is.GreaterThan(1));

    [Test]
    public void Then_the_Segments_read_only_rise() =>
        Assert.That(_seen.Select(r => r.Read), Is.Ordered.Ascending);

    [Test]
    public void Then_the_total_it_reports_is_the_Segments_in_the_slice() =>
        Assert.That(_seen.Select(r => r.Total).OfType<int>(), Is.Not.Empty.And.All.EqualTo(_read));

    [Test]
    public void Then_it_ends_having_read_them_all() =>
        Assert.That(_seen[^1], Is.EqualTo(new SliceReading(_read, _read)));
}
